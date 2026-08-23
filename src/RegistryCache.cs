using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AddinFinder
{
    /// <summary>
    /// Last-known registry entries, kept per publisher.
    ///
    /// Three things need this:
    ///
    /// - A publisher whose list cannot be read must not disappear from the pad. Their addins are
    ///   shown from cache, marked stale, rather than the user being told nothing exists.
    /// - An addin that HAS been withdrawn still needs a name and description to show to the person
    ///   who has it installed. A bare id is a poor way to tell someone what they are running.
    /// - A first refresh with no network shows the last known list instead of an empty pad.
    ///
    /// The cache is never authoritative about what is installed -- installed.v2.json and the disk
    /// are, and they are checked independently.
    /// </summary>
    public class RegistryCache
    {
        private readonly string _path;
        private Dictionary<string, List<RegistryAddin>> _byPublisher;

        /// <summary>
        /// Entries that were in a publisher's list and are not any more, keyed by addin id.
        ///
        /// The second purpose above cannot be served by the per-publisher lists alone. A refresh
        /// replaces a publisher's list wholesale, and that happens BEFORE anyone asks what became of
        /// an installed addin -- so by the time the question is put, the delisted entry has already
        /// been overwritten and only its id survives. Someone whose addin has just been withdrawn is
        /// exactly the person who needs to read what it was and who wrote it, and they would have
        /// been shown a bare folder name.
        ///
        /// Deliberately NOT stored among the per-publisher entries: those are read back as a
        /// publisher's current list, so a withdrawn addin filed there would be offered for
        /// installation again. It lives under its own top-level key for the same reason setupAddins
        /// does -- a shape an older reader would misunderstand does not go where it will find it.
        /// </summary>
        private Dictionary<string, RegistryAddin> _retired;

        public RegistryCache(string storeDir)
        {
            _path = Path.Combine(storeDir, "registry-cache.json");
            var doc      = Read();
            _byPublisher = doc.ByPublisher;
            _retired     = doc.Retired;
        }

        /// <summary>
        /// Replace the cached list for one publisher after a successful fetch.
        ///
        /// Anything that has dropped out of their list is kept aside rather than discarded, so it can
        /// still be described to whoever has it installed. An entry that reappears is taken back off
        /// that shelf, so the store never disagrees with a list that is currently being served.
        /// </summary>
        public void Put(string publisherId, List<RegistryAddin> addins)
        {
            var incoming = new HashSet<string>(addins.Select(a => a.Id), StringComparer.OrdinalIgnoreCase);

            List<RegistryAddin> previous;
            if (_byPublisher.TryGetValue(publisherId, out previous))
                foreach (var a in previous.Where(a => a.Id.Length > 0 && !incoming.Contains(a.Id)))
                    _retired[a.Id] = Clone(a);

            foreach (string id in incoming) _retired.Remove(id);

            _byPublisher[publisherId] = addins.Select(Clone).ToList();
            Write();
        }

        /// <summary>Cached entries for a publisher, marked FromCache. Empty if never fetched.</summary>
        public List<RegistryAddin> Get(string publisherId)
        {
            List<RegistryAddin> list;
            if (!_byPublisher.TryGetValue(publisherId, out list)) return new List<RegistryAddin>();
            return list.Select(a => { var c = Clone(a); c.FromCache = true; return c; }).ToList();
        }

        /// <summary>
        /// The last-known entry for an id from any publisher, or null. Used to describe an addin that
        /// is installed but no longer listed anywhere -- so entries a publisher has since dropped are
        /// searched too, and are the whole point of the search.
        /// </summary>
        public RegistryAddin? Find(string addinId)
        {
            foreach (var pair in _byPublisher)
            {
                var hit = pair.Value.FirstOrDefault(a => a.Id == addinId);
                if (hit != null) { var c = Clone(hit); c.FromCache = true; return c; }
            }

            RegistryAddin retired;
            if (_retired.TryGetValue(addinId, out retired))
            {
                var c = Clone(retired); c.FromCache = true; return c;
            }
            return null;
        }

        public bool Has(string publisherId) => _byPublisher.ContainsKey(publisherId);

        /// <summary>
        /// Every publisher we have ever cached. Used when the root registry itself cannot be read:
        /// we no longer know who the publishers are, so the last known set is all there is.
        /// </summary>
        public IEnumerable<string> PublisherIds => _byPublisher.Keys.ToList();

        private static RegistryAddin Clone(RegistryAddin a) => new RegistryAddin
        {
            Id = a.Id, Name = a.Name, Description = a.Description, Author = a.Author,
            AuthorUrl = a.AuthorUrl, License = a.License, Category = a.Category,
            Version = a.Version, TargetFramework = a.TargetFramework,
            DownloadUrls = new List<string>(a.DownloadUrls), DownloadZipUrl = a.DownloadZipUrl,
            AddinFileUrl = a.AddinFileUrl, HomepageUrl = a.HomepageUrl, ChangelogUrl = a.ChangelogUrl,
            Fork = a.Fork, UpstreamUrl = a.UpstreamUrl, Publisher = a.Publisher,

            // Without this an addin that installs itself comes back from cache looking like one we
            // could place: IsSetup is derived from the repo, so losing it turns Download into
            // Install, and the entry has no URLs to install from. Every cache fallback -- an
            // unreachable publisher, no network at all -- went through here.
            GithubRepo = a.GithubRepo,

            Status = a.Status, StatusNote = a.StatusNote, ReplacedBy = a.ReplacedBy,
        };

        private RegistryCacheDoc Read()
        {
            try
            {
                if (!File.Exists(_path)) return new RegistryCacheDoc();
                return SimpleJsonParser.ParseRegistryCache(File.ReadAllText(_path, Encoding.UTF8));
            }
            catch { return new RegistryCacheDoc(); }
        }

        private void Write()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path,
                    SimpleJsonParser.SerialiseRegistryCache(_byPublisher, _retired), Encoding.UTF8);
            }
            catch { /* a cache we cannot write is not worth failing a refresh over */ }
        }
    }

    /// <summary>registry-cache.json as read from disk: current lists, plus what has been dropped.</summary>
    public class RegistryCacheDoc
    {
        public Dictionary<string, List<RegistryAddin>> ByPublisher { get; set; } =
            new Dictionary<string, List<RegistryAddin>>();

        public Dictionary<string, RegistryAddin> Retired { get; set; } =
            new Dictionary<string, RegistryAddin>(StringComparer.OrdinalIgnoreCase);
    }
}
