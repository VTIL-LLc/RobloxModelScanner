using System;
using System.Collections.Generic;

namespace RMS.Configuration.Site.Infrastructure
{
    /// <summary>
    /// Every manifest the deployment is described by, as a list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The addresses travel with the list so that the page can show what the service is actually reading. A
    /// console pointed at the wrong checkout answers completely and wrongly, and the paths are the only thing
    /// that makes that visible without opening a file.
    /// </para>
    /// <para>
    /// The counts are read from the service rather than recomputed here. The service is the thing that decides
    /// what "present" and "an error" mean; a second implementation of that decision would be a second answer,
    /// and the page is the worst place to keep the second answer, because it is the one an operator believes.
    /// </para>
    /// </remarks>
    public sealed class ManifestListView
    {
        /// <summary>Gets or sets the repository root the service is resolving against.</summary>
        public string RepositoryRoot { get; set; }

        /// <summary>Gets or sets the directory the hand-written manifests are read from.</summary>
        public string ManifestDirectory { get; set; }

        /// <summary>Gets or sets the plan file the service is reading.</summary>
        public string PlanFile { get; set; }

        /// <summary>Gets or sets the manifests, in the order the deployer reads them.</summary>
        public List<ManifestFileView> Files { get; set; }

        /// <summary>Gets or sets the problems about the addresses rather than about one file.</summary>
        public List<ManifestProblemView> Problems { get; set; }

        /// <summary>Gets or sets a sentence describing the state of the deployment description.</summary>
        public string Summary { get; set; }

        /// <summary>Gets or sets how many of the files are present.</summary>
        public int PresentCount { get; set; }

        /// <summary>Gets or sets how many errors there are across the files that were parsed.</summary>
        public int ErrorCount { get; set; }
    }

    /// <summary>
    /// One manifest as a list entry, without its contents.
    /// </summary>
    /// <remarks>
    /// A file that was not parsed reports a negative error and warning count. That is the service saying it was
    /// not asked, which is a different claim from "nothing is wrong", and the page renders the two differently
    /// because an operator acts on them differently.
    /// </remarks>
    public sealed class ManifestFileView
    {
        /// <summary>Gets or sets which manifest this is, as the service names it.</summary>
        public string Kind { get; set; }

        /// <summary>Gets or sets the file name.</summary>
        public string Name { get; set; }

        /// <summary>Gets or sets the absolute path.</summary>
        public string Path { get; set; }

        /// <summary>Gets or sets a value indicating whether the file exists.</summary>
        public bool Exists { get; set; }

        /// <summary>Gets or sets why the file could not be read, when it could not.</summary>
        public string Unreadable { get; set; }

        /// <summary>Gets or sets the file's length in bytes.</summary>
        public long Length { get; set; }

        /// <summary>Gets or sets when the file was last written, in UTC.</summary>
        public DateTime? ModifiedUtc { get; set; }

        /// <summary>Gets or sets the SHA-256 of the file's bytes.</summary>
        public string Sha256 { get; set; }

        /// <summary>Gets or sets how many sections the file declares.</summary>
        public int SectionCount { get; set; }

        /// <summary>Gets or sets how many errors were found, or a negative number when the file was not parsed.</summary>
        public int ErrorCount { get; set; }

        /// <summary>Gets or sets how many warnings were found, or a negative number when the file was not parsed.</summary>
        public int WarningCount { get; set; }

        /// <summary>Gets or sets a value indicating whether the file may be written back.</summary>
        public bool Editable { get; set; }

        /// <summary>Gets or sets a sentence describing the file.</summary>
        public string Summary { get; set; }
    }

    /// <summary>
    /// One hand-written manifest, read and checked.
    /// </summary>
    /// <remarks>
    /// Both readings arrive together: the lines exactly as written, for the editor, and the sections as the
    /// deployer parses them, for everything else. The page never re-derives one from the other, so the text an
    /// operator edits and the table they read cannot describe two different files.
    /// </remarks>
    public sealed class ManifestDocumentView
    {
        /// <summary>Gets or sets which manifest this is, as the service names it.</summary>
        public string Kind { get; set; }

        /// <summary>Gets or sets the file name.</summary>
        public string Name { get; set; }

        /// <summary>Gets or sets the absolute path the document was read from.</summary>
        public string Path { get; set; }

        /// <summary>Gets or sets a value indicating whether the file exists.</summary>
        public bool Exists { get; set; }

        /// <summary>Gets or sets why the file could not be read, when it could not.</summary>
        public string Unreadable { get; set; }

        /// <summary>Gets or sets the file's length in bytes.</summary>
        public long Length { get; set; }

        /// <summary>Gets or sets when the file was last written, in UTC.</summary>
        public DateTime? ModifiedUtc { get; set; }

        /// <summary>Gets or sets the SHA-256 of the file's bytes, which is what a write is conditioned on.</summary>
        public string Sha256 { get; set; }

        /// <summary>Gets or sets the line ending the file uses.</summary>
        public string LineEnding { get; set; }

        /// <summary>Gets or sets every line, in order.</summary>
        public List<ManifestLineView> Lines { get; set; }

        /// <summary>Gets or sets every section, in the order the headers appeared.</summary>
        public List<ManifestSectionView> Sections { get; set; }

        /// <summary>Gets or sets every problem found, in severity then line order.</summary>
        public List<ManifestProblemView> Problems { get; set; }

        /// <summary>Gets or sets a value indicating whether the document may be written back.</summary>
        public bool Editable { get; set; }

        /// <summary>Gets or sets how many sections the document declares.</summary>
        public int SectionCount { get; set; }

        /// <summary>Gets or sets how many errors were found.</summary>
        public int ErrorCount { get; set; }

        /// <summary>Gets or sets how many warnings were found.</summary>
        public int WarningCount { get; set; }
    }

    /// <summary>
    /// One line of a manifest, as it was written.
    /// </summary>
    /// <remarks>
    /// The text is verbatim, including its indentation and the space around the <c>=</c>. It is what the editor
    /// is given, so a save that changed nothing produces no diff, and the operator reviewing a change is looking
    /// at their own formatting rather than the console's.
    /// </remarks>
    public sealed class ManifestLineView
    {
        /// <summary>Gets or sets the one-based line number.</summary>
        public int Number { get; set; }

        /// <summary>Gets or sets what the line is: a blank, a comment, a header, an entry or a malformed line.</summary>
        public string Kind { get; set; }

        /// <summary>Gets or sets the line's text, without its terminator.</summary>
        public string Text { get; set; }

        /// <summary>Gets or sets the section the line belongs to, or an empty string before the first header.</summary>
        public string Section { get; set; }

        /// <summary>Gets or sets the key, for an entry.</summary>
        public string Key { get; set; }

        /// <summary>Gets or sets the value, for an entry.</summary>
        public string Value { get; set; }

        /// <summary>Gets or sets the platform the key is qualified for.</summary>
        public string Platform { get; set; }

        /// <summary>Gets or sets what is wrong with the line, which is carried on the document's problem list too.</summary>
        public string Problem { get; set; }

        /// <summary>Gets the line's number and text, for a problem list that quotes what it is about.</summary>
        /// <returns>The quotation.</returns>
        public string Quoted()
        {
            return Number.ToString(System.Globalization.CultureInfo.InvariantCulture) + ": " + (Text ?? string.Empty);
        }
    }

    /// <summary>
    /// One section of a manifest, as the deployer would read it.
    /// </summary>
    /// <remarks>
    /// A section is one requirement, one build step or one service. The identifier is what the deployer reports
    /// in every message about it, so it is carried as written rather than rebuilt from the first entry.
    /// </remarks>
    public sealed class ManifestSectionView
    {
        /// <summary>Gets or sets the identifier, which is the text inside the brackets.</summary>
        public string Id { get; set; }

        /// <summary>Gets or sets the one-based line the header was written on.</summary>
        public int Line { get; set; }

        /// <summary>Gets or sets the value of the section's <c>kind</c>, or an empty string when it has none.</summary>
        public string Kind { get; set; }

        /// <summary>Gets or sets the value of the section's <c>platforms</c>, or an empty string when it applies to all.</summary>
        public string Platforms { get; set; }

        /// <summary>Gets or sets the short description the page shows beside the identifier.</summary>
        public string Summary { get; set; }

        /// <summary>Gets or sets every setting the section carries.</summary>
        public List<ManifestEntryView> Entries { get; set; }

        /// <summary>Gets or sets every problem attributed to this section.</summary>
        public List<ManifestProblemView> Problems { get; set; }
    }

    /// <summary>
    /// One <c>key = value</c> pair of a manifest section, as the deployer would read it.
    /// </summary>
    /// <remarks>
    /// This is the parsed view rather than the written one: the platform qualification has been taken off the
    /// front, so <c>windows.url</c> and <c>url</c> are one field with two possible qualifications and a table of
    /// a section's settings is a table of settings rather than a table of spellings.
    /// </remarks>
    public sealed class ManifestEntryView
    {
        /// <summary>Gets or sets the key with its platform qualification removed.</summary>
        public string Key { get; set; }

        /// <summary>Gets or sets the value as written.</summary>
        public string Value { get; set; }

        /// <summary>Gets or sets the platform the entry is qualified for, or an empty string for every platform.</summary>
        public string Platform { get; set; }

        /// <summary>Gets or sets the one-based line the entry was written on.</summary>
        public int Line { get; set; }

        /// <summary>Gets or sets a value indicating whether the deployer reads this key at all.</summary>
        public bool Recognised { get; set; }
    }

    /// <summary>
    /// One thing wrong with a manifest, and where it is.
    /// </summary>
    /// <remarks>
    /// Every problem carries the line it was found on, because a message without one sends an operator looking
    /// through a hundred and twenty lines of comments for a key they cannot see. A problem about a whole file
    /// rather than a line reports line zero, which the page renders as "the file".
    /// </remarks>
    public sealed class ManifestProblemView
    {
        /// <summary>Gets or sets how badly the problem matters: <c>Error</c>, <c>Warning</c> or <c>Note</c>.</summary>
        public string Severity { get; set; }

        /// <summary>Gets or sets the one-based line, or zero when the problem is about the file.</summary>
        public int Line { get; set; }

        /// <summary>Gets or sets the section the problem is in, or an empty string.</summary>
        public string Section { get; set; }

        /// <summary>Gets or sets the key the problem is about, or an empty string.</summary>
        public string Key { get; set; }

        /// <summary>Gets or sets the sentence describing the problem.</summary>
        public string Message { get; set; }
    }

    /// <summary>
    /// What happened to a write, or to a check that was asked not to write.
    /// </summary>
    /// <remarks>
    /// A refusal here is a successful response carrying a result. "The file is one line wrong" is a thing the
    /// page has to render line by line, and delivering it as a transport failure would mean rendering an error
    /// body as though it were a result. Only a guard refusal and a lost precondition arrive as HTTP errors.
    /// </remarks>
    public sealed class ManifestWriteResultView
    {
        /// <summary>Gets or sets a value indicating whether the file was written.</summary>
        public bool Written { get; set; }

        /// <summary>Gets or sets a value indicating whether the request was to check rather than to write.</summary>
        public bool CheckedOnly { get; set; }

        /// <summary>Gets or sets the manifest's name.</summary>
        public string Name { get; set; }

        /// <summary>Gets or sets the absolute path the write was made to.</summary>
        public string Path { get; set; }

        /// <summary>Gets or sets the SHA-256 of the file as it now stands.</summary>
        public string Sha256 { get; set; }

        /// <summary>Gets or sets the path of the copy of the previous contents, when one was made.</summary>
        public string BackupPath { get; set; }

        /// <summary>Gets or sets a sentence describing what happened.</summary>
        public string Message { get; set; }

        /// <summary>Gets or sets the document as it stands after the request.</summary>
        public ManifestDocumentView Document { get; set; }

        /// <summary>Gets or sets every problem found in the text that was sent.</summary>
        public List<ManifestProblemView> Problems { get; set; }
    }

    /// <summary>
    /// A request to write a manifest, or to check one without writing it.
    /// </summary>
    /// <remarks>
    /// The whole file is sent rather than a change to it. These files are a hundred and twenty lines each, the
    /// operator is looking at all of them, and a description of a diff would be a second syntax to get wrong on
    /// top of the one being edited -- and one that could not express a comment.
    /// </remarks>
    public sealed class ManifestWriteRequestView
    {
        /// <summary>Gets or sets the text the file should have.</summary>
        public string Text { get; set; }

        /// <summary>Gets or sets the SHA-256 of the file as the page read it.</summary>
        public string ExpectedSha256 { get; set; }
    }

    /// <summary>
    /// The generated plan, as the installer will read it.
    /// </summary>
    /// <remarks>
    /// A plan that is absent is reported in a sentence rather than as a failure. A machine that has been
    /// provisioned but not yet deployed has no plan, and that is an ordinary state for this page to describe --
    /// and one an operator is quite likely to be looking at it in order to diagnose.
    /// </remarks>
    public sealed class PlanView
    {
        /// <summary>Gets or sets a value indicating whether the plan could be read.</summary>
        public bool Available { get; set; }

        /// <summary>Gets or sets why the plan could not be read, when it could not.</summary>
        public string Detail { get; set; }

        /// <summary>Gets or sets the absolute path of the plan file.</summary>
        public string Path { get; set; }

        /// <summary>Gets or sets the plan's schema version.</summary>
        public int Version { get; set; }

        /// <summary>Gets or sets the platform the plan was written for.</summary>
        public string Platform { get; set; }

        /// <summary>Gets or sets the repository root the plan was generated against.</summary>
        public string Root { get; set; }

        /// <summary>Gets or sets the directory the deployment's output lives under.</summary>
        public string OutputRoot { get; set; }

        /// <summary>Gets or sets the root of the run store.</summary>
        public string LogDirectory { get; set; }

        /// <summary>Gets or sets the supervisor the installer will register.</summary>
        public string HostExecutable { get; set; }

        /// <summary>Gets or sets the services the plan declares.</summary>
        public List<PlanServiceView> Services { get; set; }

        /// <summary>Gets or sets every problem found in the plan.</summary>
        public List<ManifestProblemView> Problems { get; set; }

        /// <summary>Gets or sets a sentence describing the plan.</summary>
        public string Summary { get; set; }
    }

    /// <summary>
    /// One service as the generated plan describes it.
    /// </summary>
    /// <remarks>
    /// This is the plan's reading of a service, not the manifest's: every <c>%ROOT%</c> has been expanded, every
    /// platform-qualified value has been chosen, every default has been filled in, and the arguments are a list
    /// rather than a semicolon separated string. Showing it beside the manifest section it came from is the
    /// point of the plan page: it is where an operator sees what their edit actually became.
    /// </remarks>
    public sealed class PlanServiceView
    {
        /// <summary>Gets or sets the identifier, which is the manifest's section name.</summary>
        public string Id { get; set; }

        /// <summary>Gets or sets the service's name.</summary>
        public string Name { get; set; }

        /// <summary>Gets or sets the description.</summary>
        public string Description { get; set; }

        /// <summary>Gets or sets the executable, which is empty for a web site the platform serves.</summary>
        public string Executable { get; set; }

        /// <summary>Gets or sets the arguments, one entry each.</summary>
        public List<string> Arguments { get; set; }

        /// <summary>Gets or sets the working directory.</summary>
        public string WorkingDirectory { get; set; }

        /// <summary>Gets or sets the restart policy's name.</summary>
        public string Restart { get; set; }

        /// <summary>Gets or sets the seconds before the first restart.</summary>
        public int RestartDelaySeconds { get; set; }

        /// <summary>Gets or sets the ceiling the delay doubles up to.</summary>
        public int MaximumRestartDelaySeconds { get; set; }

        /// <summary>Gets or sets the seconds between a stop request and a kill.</summary>
        public int StopTimeoutSeconds { get; set; }

        /// <summary>Gets or sets the line written to the child's input to ask it to stop.</summary>
        public string ShutdownLine { get; set; }

        /// <summary>Gets or sets the directory the platform serves, for a web site entry.</summary>
        public string Website { get; set; }

        /// <summary>Gets or sets the port, as text, because the plan carries it that way.</summary>
        public string Port { get; set; }

        /// <summary>Gets or sets the platforms the entry applies to.</summary>
        public string Platforms { get; set; }

        /// <summary>Gets or sets the account to run as.</summary>
        public string User { get; set; }

        /// <summary>Gets or sets the environment the entry declares, in the order the plan sorted it.</summary>
        public List<PlanVariableView> Environment { get; set; }

        /// <summary>Gets or sets the directory this service's logs are written to.</summary>
        public string LogDirectory { get; set; }

        /// <summary>Gets or sets a value indicating whether the plan's own platform list includes this entry.</summary>
        public bool AppliesToThisPlan { get; set; }

        /// <summary>Gets or sets the command the supervisor would run, for an operator who wants to try it by hand.</summary>
        public string Command { get; set; }

        /// <summary>Gets or sets every problem attributed to this entry.</summary>
        public List<ManifestProblemView> Problems { get; set; }
    }

    /// <summary>
    /// One environment variable the plan sets for a service.
    /// </summary>
    /// <remarks>
    /// The value is reported as the plan carries it, resolved and not redacted, because the plan is a file on
    /// the machine that anybody who can read the deployment can read. What is deliberately absent is any key
    /// this stack keeps in a file of its own: the plan declares the path of a token file and never its contents.
    /// </remarks>
    public sealed class PlanVariableView
    {
        /// <summary>Gets or sets the variable's name.</summary>
        public string Key { get; set; }

        /// <summary>Gets or sets the variable's value.</summary>
        public string Value { get; set; }
    }

    /// <summary>
    /// The whole format, as the deployer reads it.
    /// </summary>
    /// <remarks>
    /// This exists so that the editor can offer the right keys while an operator is typing rather than after
    /// they have saved and been told. It is published by the service, computed from one table, and read by the
    /// page: a second copy of the vocabulary living here would be a copy that could accept a key the deployer
    /// ignores, which is exactly the failure this console exists to prevent.
    /// </remarks>
    public sealed class VocabularyView
    {
        /// <summary>Gets or sets the vocabularies, one per manifest kind.</summary>
        public List<VocabularySectionView> Sections { get; set; }

        /// <summary>Gets or sets the platform names a key may be qualified with.</summary>
        public List<string> Platforms { get; set; }

        /// <summary>Gets or sets the ceilings the deployer's parser enforces, in the deployer's own names.</summary>
        public List<DeployerLimitView> Limits { get; set; }

        /// <summary>Gets or sets a sentence describing the format.</summary>
        public string Summary { get; set; }
    }

    /// <summary>
    /// The vocabulary one manifest kind is written in.
    /// </summary>
    /// <remarks>
    /// The section kinds and the keys are one description of the format, held by the service, and the page
    /// reads them from there. A page with its own copy of the list would be a page that accepted a kind the
    /// validator refused, which is the kind of disagreement that makes a console untrustworthy in exactly the
    /// case it exists for.
    /// </remarks>
    public sealed class VocabularySectionView
    {
        /// <summary>Gets or sets which manifest this vocabulary describes, as the service names it.</summary>
        public string Kind { get; set; }

        /// <summary>Gets or sets the file name the deployer looks for.</summary>
        public string FileName { get; set; }

        /// <summary>Gets or sets a sentence saying what the file is for.</summary>
        public string Summary { get; set; }

        /// <summary>Gets or sets a sentence saying what a section of this file is.</summary>
        public string SectionSummary { get; set; }

        /// <summary>Gets or sets the values the section's <c>kind</c> may take, when the set is closed.</summary>
        public List<string> Kinds { get; set; }

        /// <summary>Gets or sets the setting names a section of this file may carry.</summary>
        public List<VocabularyEntryView> Keys { get; set; }
    }

    /// <summary>
    /// One setting name a manifest section may carry.
    /// </summary>
    /// <remarks>
    /// The vocabulary is published by the service rather than written into the page, because the page has to be
    /// able to say what a key is for as well as that it exists. An operator told that <c>strip</c> is accepted
    /// but not what it counts writes a plausible number and wonders later why the archive extracted oddly.
    /// </remarks>
    public sealed class VocabularyEntryView
    {
        /// <summary>Gets or sets the setting's name, unqualified.</summary>
        public string Key { get; set; }

        /// <summary>Gets or sets a value indicating whether the key may be qualified with a platform.</summary>
        public bool PerPlatform { get; set; }

        /// <summary>Gets or sets a value indicating whether the deployer refuses the section without the key.</summary>
        public bool Required { get; set; }

        /// <summary>Gets or sets the values the key accepts, when it accepts a closed set.</summary>
        public List<string> Values { get; set; }

        /// <summary>Gets or sets the default the deployer applies when the key is absent.</summary>
        public string Default { get; set; }

        /// <summary>Gets or sets the longest value the deployer can hold for this key, in characters.</summary>
        public int MaximumLength { get; set; }

        /// <summary>Gets or sets a sentence saying what the key is for.</summary>
        public string Summary { get; set; }
    }

    /// <summary>
    /// One of the fixed-size buffer limits the deployer's parser enforces.
    /// </summary>
    /// <remarks>
    /// The C constant's name travels with the number so that an operator who has just been refused by the
    /// deployer has somewhere to look. "Too long" and "too long for RMS_MAX_VALUE" are the same length, and only
    /// one of them can be found in the source.
    /// </remarks>
    public sealed class DeployerLimitView
    {
        /// <summary>Gets or sets the C constant's name.</summary>
        public string Name { get; set; }

        /// <summary>Gets or sets the limit.</summary>
        public int Value { get; set; }

        /// <summary>Gets or sets a sentence saying what the limit applies to.</summary>
        public string Summary { get; set; }
    }
}
