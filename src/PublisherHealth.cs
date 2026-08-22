using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AddinFinder
{
    /// <summary>How the last attempt to read a publisher's addin list went.</summary>
    public enum FetchOutcome
    {
        /// <summary>The list was read.</summary>
        Ok,

        /// <summary>No answer at all -- no network, DNS, timeout, TLS. Says NOTHING about the publisher.</summary>
        Unreachable,

        /// <summary>The server answered, and the file is not there. A definitive answer, but see below.</summary>
        NotFound,

        /// <summary>Answered with content we could not parse.</summary>
        Malformed,
    }

    /// <summary>Per-publisher fetch history, persisted so a verdict can span sessions.</summary>
    public class PublisherHealthEntry
    {
        public string Id               { get; set; } = "";
        public string LastOutcome      { get; set; } = FetchOutcome.Ok.ToString();

        /// <summary>yyyy-MM-dd of the last successful read, or "" if never.</summary>
        public string LastSuccess      { get; set; } = "";

        /// <summary>yyyy-MM-dd this run of consecutive 404s began, or "" if not currently 404ing.</summary>
        public string NotFoundSince    { get; set; } = "";

        /// <summary>Consecutive NotFound results. Reset by any success.</summary>
        public int    ConsecutiveNotFound { get; set; }
    }

    /// <summary>
    /// Decides how confident we are that a publisher's list has really gone away.
    ///
    /// This exists because "the publisher deleted their list" and "the fetch failed" are the same
    /// event from the client's side unless it is careful. Getting it wrong means a publisher's
    /// outage tells every user their addins were withdrawn -- so the verdict is deliberately slow,
    /// always staged, and never destructive. Nothing here uninstalls, hides or deletes anything a
    /// user already has; it only changes what the pad says.
    ///
    /// A 404 is treated as more informative than no answer -- a server that answered told us the
    /// file is not there -- but still not conclusive alone. A renamed default branch produces the
    /// identical 404 to a deleted repository, and main-vs-master has already caught this design out
    /// once.
    /// </summary>
    public class PublisherHealth
    {
        /// <summary>Consecutive 404s before a publisher is described as presumed withdrawn.</summary>
        public const int WithdrawnAfterNotFoundCount = 3;

        /// <summary>...and only if the first of them was at least this many days ago.</summary>
        public const int WithdrawnAfterDays = 7;

        private readonly string _path;
        private List<PublisherHealthEntry> _entries;

        public PublisherHealth(string storeDir)
        {
            _path = Path.Combine(storeDir, "publisher-health.json");
            _entries = Read();
        }

        public PublisherHealthEntry For(string publisherId)
        {
            var e = _entries.FirstOrDefault(x => x.Id == publisherId);
            if (e == null)
            {
                e = new PublisherHealthEntry { Id = publisherId };
                _entries.Add(e);
            }
            return e;
        }

        public void Record(string publisherId, FetchOutcome outcome, DateTime today)
        {
            var e = For(publisherId);
            e.LastOutcome = outcome.ToString();

            if (outcome == FetchOutcome.Ok)
            {
                e.LastSuccess          = today.ToString("yyyy-MM-dd");
                e.NotFoundSince        = "";
                e.ConsecutiveNotFound  = 0;
            }
            else if (outcome == FetchOutcome.NotFound)
            {
                if (e.ConsecutiveNotFound == 0) e.NotFoundSince = today.ToString("yyyy-MM-dd");
                e.ConsecutiveNotFound++;
            }
            // Unreachable and Malformed deliberately do not advance the withdrawal count. Neither
            // tells us anything about the publisher's intent.

            Write();
        }

        /// <summary>
        /// True once repeated 404s, spread over enough days, make a deleted or renamed list the only
        /// reasonable reading. Still only changes wording -- see the class comment.
        /// </summary>
        public bool IsPresumedWithdrawn(string publisherId, DateTime today)
        {
            var e = For(publisherId);
            if (e.ConsecutiveNotFound < WithdrawnAfterNotFoundCount) return false;
            if (string.IsNullOrEmpty(e.NotFoundSince)) return false;

            DateTime since;
            if (!DateTime.TryParse(e.NotFoundSince, out since)) return false;
            return (today - since).TotalDays >= WithdrawnAfterDays;
        }

        // ---- persistence ----------------------------------------------------------------------

        private List<PublisherHealthEntry> Read()
        {
            try
            {
                if (!File.Exists(_path)) return new List<PublisherHealthEntry>();
                return SimpleJsonParser.ParsePublisherHealth(File.ReadAllText(_path, Encoding.UTF8));
            }
            catch { return new List<PublisherHealthEntry>(); }
        }

        private void Write()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, SimpleJsonParser.SerialisePublisherHealth(_entries), Encoding.UTF8);
            }
            catch { /* health tracking is advisory; never fail a refresh over it */ }
        }
    }
}
