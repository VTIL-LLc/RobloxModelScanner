using System;
using System.Collections.Generic;

namespace RMS.Devops.Site.Infrastructure
{
    /// <summary>
    /// One service, as the DevOps service describes it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The plan and the run store are merged on the service's side and arrive here already reconciled, so this
    /// site never has to decide which of two sources it believes. That matters because the decision is not
    /// obvious: the plan says what a service is meant to be, the run store says what it did, and a page that
    /// made that distinction itself would make it slightly differently on each of the six pages that show a
    /// service.
    /// </para>
    /// <para>
    /// The state arrives as a word rather than as a number. The word is what an operator reads and what a
    /// stylesheet keys its colour from, and turning it into a number on the way in would only mean turning it
    /// back.
    /// </para>
    /// </remarks>
    public sealed class ServiceSnapshot
    {
        /// <summary>Gets or sets the service's name, which is also the key everything else is found by.</summary>
        public string Name { get; set; }

        /// <summary>Gets or sets the identifier the deployer gave the service.</summary>
        public string PlanId { get; set; }

        /// <summary>Gets or sets the service's description.</summary>
        public string Description { get; set; }

        /// <summary>Gets or sets the command line the supervisor runs.</summary>
        public string Command { get; set; }

        /// <summary>Gets or sets the directory the service runs in.</summary>
        public string WorkingDirectory { get; set; }

        /// <summary>Gets or sets the directory the service's history is recorded under.</summary>
        public string LogDirectory { get; set; }

        /// <summary>Gets or sets the program the service runs.</summary>
        public string Executable { get; set; }

        /// <summary>Gets or sets the site the service serves, when it serves one.</summary>
        public string Website { get; set; }

        /// <summary>Gets or sets the port the service listens on.</summary>
        public string Port { get; set; }

        /// <summary>Gets or sets the restart policy.</summary>
        public string Restart { get; set; }

        /// <summary>Gets or sets how many runs are kept before the oldest are removed.</summary>
        public int RetainedRuns { get; set; }

        /// <summary>Gets or sets how many runs are recorded.</summary>
        public int RunCount { get; set; }

        /// <summary>Gets or sets a value indicating whether the current plan still describes this service.</summary>
        public bool IsInPlan { get; set; }

        /// <summary>Gets or sets a value indicating whether the service serves a site.</summary>
        public bool IsWebsite { get; set; }

        /// <summary>Gets or sets the state, as a word.</summary>
        public string State { get; set; }

        /// <summary>Gets or sets the sentence explaining the state.</summary>
        public string StateDescription { get; set; }

        /// <summary>Gets or sets the identifier of the newest run.</summary>
        public string LatestRun { get; set; }

        /// <summary>Gets or sets the status of the newest run.</summary>
        public string LatestStatus { get; set; }

        /// <summary>Gets or sets when the newest run was created.</summary>
        public DateTime? LatestCreated { get; set; }

        /// <summary>Gets or sets when the newest run's process was started.</summary>
        public DateTime? LatestStarted { get; set; }

        /// <summary>Gets or sets when the newest run finished.</summary>
        public DateTime? LatestCompleted { get; set; }

        /// <summary>Gets or sets the exit code of the newest run's last attempt.</summary>
        public int? LatestExitCode { get; set; }

        /// <summary>Gets or sets how many start attempts the newest run took.</summary>
        public int LatestAttempts { get; set; }

        /// <summary>Gets or sets how long the newest run took, as the supervisor recorded it.</summary>
        public string LatestRuntimeText { get; set; }

        /// <summary>Gets or sets how large the newest run's log is.</summary>
        public long LatestLogBytes { get; set; }

        /// <summary>Gets or sets the most recent runs, newest first.</summary>
        public List<RunSnapshot> RecentRuns { get; set; }
    }

    /// <summary>
    /// One run of one service.
    /// </summary>
    public sealed class RunSnapshot
    {
        /// <summary>Gets or sets the run's identifier.</summary>
        public string Run { get; set; }

        /// <summary>Gets or sets the service the run belongs to.</summary>
        public string Service { get; set; }

        /// <summary>Gets or sets the run's position in its service's sequence.</summary>
        public long Sequence { get; set; }

        /// <summary>Gets or sets the status, as a word.</summary>
        public string Status { get; set; }

        /// <summary>Gets or sets a value indicating whether the run has finished.</summary>
        public bool IsComplete { get; set; }

        /// <summary>Gets or sets when the run was created.</summary>
        public DateTime? Created { get; set; }

        /// <summary>Gets or sets when the run's process was first started.</summary>
        public DateTime? Started { get; set; }

        /// <summary>Gets or sets when the record was last written.</summary>
        public DateTime? Updated { get; set; }

        /// <summary>Gets or sets when the run finished.</summary>
        public DateTime? Completed { get; set; }

        /// <summary>Gets or sets how long the run took, in seconds.</summary>
        public long RuntimeSeconds { get; set; }

        /// <summary>Gets or sets how long the run took, as the supervisor recorded it.</summary>
        public string RuntimeText { get; set; }

        /// <summary>Gets or sets how many times the process was started.</summary>
        public int Attempts { get; set; }

        /// <summary>Gets or sets the exit code, when the run ended by exiting.</summary>
        public int? ExitCode { get; set; }

        /// <summary>Gets or sets how large the run's log is.</summary>
        public long LogBytes { get; set; }
    }

    /// <summary>
    /// A page of runs.
    /// </summary>
    public sealed class RunPage
    {
        /// <summary>Gets or sets the service the page was drawn from, or an empty string for every service.</summary>
        public string Service { get; set; }

        /// <summary>Gets or sets how many runs were skipped.</summary>
        public int Skip { get; set; }

        /// <summary>Gets or sets how many runs the page holds.</summary>
        public int Take { get; set; }

        /// <summary>Gets or sets how many runs exist in total.</summary>
        public int Total { get; set; }

        /// <summary>Gets or sets the runs, newest first.</summary>
        public List<RunSnapshot> Runs { get; set; }
    }

    /// <summary>
    /// One run, with the details only its own page needs.
    /// </summary>
    public sealed class RunDetail
    {
        /// <summary>Gets or sets the run's summary.</summary>
        public RunSnapshot Summary { get; set; }

        /// <summary>Gets or sets the command line that was run.</summary>
        public string Command { get; set; }

        /// <summary>Gets or sets the process identifier, when the run had one.</summary>
        public int? ProcessId { get; set; }

        /// <summary>Gets or sets the path of the log file.</summary>
        public string LogFile { get; set; }

        /// <summary>Gets or sets the end of the log, as the service read it.</summary>
        public string LogTail { get; set; }

        /// <summary>Gets or sets a value indicating whether the tail is only part of the log.</summary>
        public bool LogTruncated { get; set; }
    }

    /// <summary>
    /// A slice of a run's log.
    /// </summary>
    public sealed class LogSlice
    {
        /// <summary>Gets or sets the service the log belongs to.</summary>
        public string Service { get; set; }

        /// <summary>Gets or sets the run the log belongs to.</summary>
        public string Run { get; set; }

        /// <summary>Gets or sets the text of the slice.</summary>
        public string Text { get; set; }

        /// <summary>Gets or sets the byte offset the slice began at.</summary>
        public long Offset { get; set; }

        /// <summary>Gets or sets how many bytes the slice holds.</summary>
        public long Length { get; set; }

        /// <summary>Gets or sets how large the whole log is.</summary>
        public long TotalBytes { get; set; }

        /// <summary>Gets or sets a value indicating whether the slice reaches the end of the file.</summary>
        public bool Complete { get; set; }

        /// <summary>Gets or sets a value indicating whether the slice was cut short by the size limit.</summary>
        public bool LimitReached { get; set; }
    }

    /// <summary>
    /// One program the deployment published.
    /// </summary>
    /// <remarks>
    /// Every fact here was read off the artefact itself -- its file count, its size, the version in the
    /// assembly's own metadata -- which is what makes this page unable to go stale. What it cannot say is which
    /// deployment produced it, because nothing recorded that on the machine.
    /// </remarks>
    public sealed class ArtifactSnapshot
    {
        /// <summary>Gets or sets the artefact's directory name.</summary>
        public string Name { get; set; }

        /// <summary>Gets or sets the artefact's full path.</summary>
        public string Directory { get; set; }

        /// <summary>Gets or sets the name of the assembly the publish produced.</summary>
        public string Assembly { get; set; }

        /// <summary>Gets or sets the assembly's file version.</summary>
        public string Version { get; set; }

        /// <summary>Gets or sets the assembly's informational version.</summary>
        public string InformationalVersion { get; set; }

        /// <summary>Gets or sets how many bytes the artefact occupies.</summary>
        public long Bytes { get; set; }

        /// <summary>Gets or sets how many files it holds.</summary>
        public int Files { get; set; }

        /// <summary>Gets or sets when it last changed.</summary>
        public DateTime? Updated { get; set; }

        /// <summary>Gets or sets the service that runs out of this artefact, when one does.</summary>
        public string ServiceName { get; set; }

        /// <summary>Gets or sets how many runs that service has recorded.</summary>
        public int Runs { get; set; }
    }

    /// <summary>
    /// The artefact listing the service answered with.
    /// </summary>
    public sealed class ArtifactList
    {
        /// <summary>Gets or sets the publish root the artefacts were found under.</summary>
        public string Root { get; set; }

        /// <summary>Gets or sets the artefacts, most recently changed first.</summary>
        public List<ArtifactSnapshot> Artifacts { get; set; }
    }
}
