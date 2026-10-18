using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace RMS.Site.Common.Implementation
{
    /// <summary>
    /// Derives and verifies password hashes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Passwords are stored as a PBKDF2-HMAC-SHA256 derivation over a per-account random salt. The iteration
    /// count is written beside each hash rather than assumed, so raising it later re-derives on the next
    /// successful sign-in instead of locking everybody out, and an old row stays verifiable using the count it
    /// was created with.
    /// </para>
    /// <para>
    /// A derivation rather than a digest is the point. A single SHA-256 of a password can be searched at
    /// billions of candidates a second on a graphics card; a two-hundred-and-ten-thousand-iteration
    /// derivation turns each candidate into that many operations, and the cost is paid once per sign-in by a
    /// site that has a handful of operators.
    /// </para>
    /// <para>
    /// Comparison is written out here rather than delegated. .NET Framework 4.8 has no
    /// <c>CryptographicOperations.FixedTimeEquals</c>, and an early-exit comparison on a derived value leaks
    /// how many leading bytes a guess got right -- which is a slower way of arriving at the same place, but it
    /// is slower by an amount that is measurable over enough attempts.
    /// </para>
    /// </remarks>
    public static class PasswordHasher
    {
        /// <summary>
        /// The number of bytes of salt written with each hash.
        /// </summary>
        private const int SaltBytes = 16;

        /// <summary>
        /// The number of bytes each derived hash occupies.
        /// </summary>
        private const int HashBytes = 32;

        /// <summary>
        /// Derives a hash and a salt for a password.
        /// </summary>
        /// <param name="password">The password to derive from.</param>
        /// <param name="iterations">The number of PBKDF2 iterations.</param>
        /// <param name="hash">Receives the derived hash, base64 encoded.</param>
        /// <param name="salt">Receives the salt, base64 encoded.</param>
        public static void Derive(string password, int iterations, out string hash, out string salt)
        {
            if (password == null) throw new ArgumentNullException(nameof(password));

            byte[] saltBytes = new byte[SaltBytes];

            // The salt is random per password. A constant salt would make two accounts with the same password
            // store the same hash, which is the fact a precomputed table is built to exploit.
            using (RandomNumberGenerator generator = RandomNumberGenerator.Create())
            {
                generator.GetBytes(saltBytes);
            }

            hash = Convert.ToBase64String(Derive(password, saltBytes, iterations));
            salt = Convert.ToBase64String(saltBytes);
        }

        /// <summary>
        /// Verifies a password against a stored hash.
        /// </summary>
        /// <param name="password">The password supplied.</param>
        /// <param name="expectedHash">The stored hash, base64 encoded.</param>
        /// <param name="salt">The stored salt, base64 encoded.</param>
        /// <param name="iterations">The iteration count the stored hash was created with.</param>
        /// <returns><see langword="true"/> when the password matches.</returns>
        /// <remarks>
        /// A malformed stored value is a non-match rather than an exception. A row that cannot be parsed is a
        /// row nobody can sign in with, which is the safe direction, and it must not be able to fault the
        /// login page for everybody else.
        /// </remarks>
        public static bool Verify(string password, string expectedHash, string salt, int iterations)
        {
            if (password == null) return false;
            if (string.IsNullOrEmpty(expectedHash) || string.IsNullOrEmpty(salt)) return false;
            if (iterations < 1) return false;

            byte[] expected;
            byte[] saltBytes;

            try
            {
                expected = Convert.FromBase64String(expectedHash);
                saltBytes = Convert.FromBase64String(salt);
            }
            catch (FormatException)
            {
                return false;
            }

            if (expected.Length == 0 || saltBytes.Length == 0) return false;

            byte[] candidate = Derive(password, saltBytes, iterations);

            return FixedTimeEquals(candidate, expected);
        }

        /// <summary>
        /// Determines whether a password is worth accepting, and says why not when it is not.
        /// </summary>
        /// <param name="password">The password to test.</param>
        /// <param name="minimumLength">The shortest password the site accepts.</param>
        /// <returns>A sentence describing the problem, or <see langword="null"/> when there is none.</returns>
        /// <remarks>
        /// Composition rules are deliberately absent. Requiring a digit and a punctuation mark produces
        /// <c>Password1!</c> and forbids a long phrase, which is the opposite of what the requirement was for.
        /// Length is the property that correlates with resistance to being guessed, so length is what is
        /// checked, and the ceiling exists only to stop an absurd input from becoming an absurd derivation.
        /// </remarks>
        public static string DescribeWeakness(string password, int minimumLength)
        {
            if (string.IsNullOrEmpty(password))
            {
                return "Enter a password.";
            }

            if (password.Length < minimumLength)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "The password must be at least {0} characters.",
                    minimumLength);
            }

            if (password.Length > 512)
            {
                return "The password must be at most 512 characters.";
            }

            return null;
        }

        /// <summary>
        /// Compares two byte sequences without an early exit.
        /// </summary>
        /// <param name="left">The first sequence.</param>
        /// <param name="right">The second sequence.</param>
        /// <returns><see langword="true"/> when the sequences are equal.</returns>
        private static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null) return false;

            // The length difference is folded into the accumulator rather than returned from, so the loop
            // runs for the longer of the two and the time taken does not depend on where the first difference
            // is. The length itself is not a secret: it is fixed for every row this site writes.
            int difference = left.Length ^ right.Length;
            int length = Math.Max(left.Length, right.Length);

            for (int index = 0; index < length; index++)
            {
                byte a = index < left.Length ? left[index] : (byte)0;
                byte b = index < right.Length ? right[index] : (byte)0;

                difference |= a ^ b;
            }

            return difference == 0;
        }

        /// <summary>
        /// Derives a hash from an explicit salt.
        /// </summary>
        /// <param name="password">The password.</param>
        /// <param name="salt">The salt.</param>
        /// <param name="iterations">The iteration count.</param>
        /// <returns>The derived bytes.</returns>
        private static byte[] Derive(string password, byte[] salt, int iterations)
        {
            using (Rfc2898DeriveBytes derivation = new Rfc2898DeriveBytes(
                       password,
                       salt,
                       iterations,
                       HashAlgorithmName.SHA256))
            {
                return derivation.GetBytes(HashBytes);
            }
        }

        /// <summary>
        /// Generates a random password for an account the deployment creates.
        /// </summary>
        /// <param name="length">The number of characters to generate.</param>
        /// <returns>The password.</returns>
        /// <remarks>
        /// The alphabet leaves out the characters that a person reads aloud or copies by hand and gets wrong:
        /// the digit one and the letter ell, the digit zero and the letter oh, and the two cases of the
        /// letters around them. The value is written once and changed immediately, so legibility matters more
        /// here than the handful of bits the excluded characters would add.
        /// </remarks>
        public static string GeneratePassword(int length)
        {
            const string alphabet = "ABCDEFGHJKMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
            const string symbols = "-_=+@#%";

            if (length < 16) length = 16;

            byte[] bytes = new byte[length];

            using (RandomNumberGenerator generator = RandomNumberGenerator.Create())
            {
                generator.GetBytes(bytes);
            }

            StringBuilder builder = new StringBuilder(length);

            for (int index = 0; index < length; index++)
            {
                // The symbol alphabet is used for roughly one character in six, which is enough entropy to
                // satisfy a policy that demands one without making the value hard to transcribe.
                bool symbol = index % 6 == 5;
                string pool = symbol ? symbols : alphabet;

                builder.Append(pool[bytes[index] % pool.Length]);
            }

            return builder.ToString();
        }
    }
}
