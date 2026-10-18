namespace RMS.Site.Common.Models
{
    /// <summary>
    /// A file a service sent, with the name and the type it is to be given.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two of the three fields are not the file. The name is what the service decided the file should be
    /// saved under -- the asset's identifier, followed by an extension for the kind of content it turned out
    /// to be -- and the type is the media type that goes with it. Both are carried rather than derived here
    /// because only the service knows what it fetched: a site that guessed the extension from the address
    /// would be guessing from a hash, and a site that guessed the type would be guessing from the extension.
    /// </para>
    /// <para>
    /// Both have been checked by the client before this is built: the name is a plain file name, and the
    /// type is one of a short list the client recognises. What arrives here is therefore safe to put on a
    /// response, which is why the handler that writes it does not check it again.
    /// </para>
    /// </remarks>
    public sealed class ServiceFile
    {
        /// <summary>
        /// Gets or sets the file's contents.
        /// </summary>
        public byte[] Content { get; set; }

        /// <summary>
        /// Gets or sets the media type the file is sent as.
        /// </summary>
        public string ContentType { get; set; }

        /// <summary>
        /// Gets or sets the name the file is sent under, without any directory part.
        /// </summary>
        public string FileName { get; set; }
    }
}
