using System.IO;
using System.Text.Json;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public sealed class TrackSectionStore
{
    private readonly string _root;
    public string RootDirectory => _root;
    public TrackSectionStore(string? root = null)
    {
        if (root is null)
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(local)) throw new InvalidOperationException("Windows did not provide a LocalAppData folder for ADT.");
            root = Path.Combine(local, "AtomicDriftTuner", "TrackSections");
        }
        _root = Path.GetFullPath(root);
    }
    private static void EnsureNoLinks(string path)
    {
        for (var dir = new DirectoryInfo(Path.GetFullPath(path)); dir is not null; dir = dir.Parent)
            if (dir.Exists && (dir.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Track storage cannot use directory links: " + dir.FullName);
    }
    public void SaveFeedback(SectionFeedbackReview review)
    {
        if (!ValidFeedback(review)) throw new InvalidDataException("Invalid section feedback.");
        var directory = Path.Combine(_root, "reviews"); EnsureNoLinks(directory);
        Directory.CreateDirectory(directory);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(review, new JsonSerializerOptions { WriteIndented = true });
        if (bytes.Length > 128 * 1024) throw new InvalidDataException("Section feedback is too large.");
        string path = Path.Combine(directory, review.Id + ".json"), temp = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
        try { File.WriteAllBytes(temp, bytes); File.Move(temp, path, overwrite: false); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public List<SectionFeedbackReview> LoadFeedback(string fingerprint, out string issue)
    {
        issue = ""; var reviews = new List<SectionFeedbackReview>();
        var directory = Path.Combine(_root, "reviews"); EnsureNoLinks(directory);
        if (!Directory.Exists(directory)) return reviews;
        int count = 0;
        foreach (var path in Directory.EnumerateFiles(directory, "*.json").Take(2001))
        {
            if (++count > 2000) { issue = "Section review history limit reached; older files are retained."; break; }
            try
            {
                var info = new FileInfo(path);
                if (info.Length > 128 * 1024 || (info.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException();
                var r = JsonSerializer.Deserialize<SectionFeedbackReview>(File.ReadAllBytes(path));
                if (r is null || !ValidFeedback(r)) throw new InvalidDataException();
                if (r.Feedback.ContextFingerprint == fingerprint) reviews.Add(r);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or ArgumentException or NullReferenceException)
            { issue = "An invalid section review was preserved and skipped."; }
        }
        return reviews.OrderByDescending(r => r.ReviewedUtc).ToList();
    }
    private static bool ValidFeedback(SectionFeedbackReview r) => r.Schema == "adt/section-feedback-review/1" && Guid.TryParseExact(r.Id, "N", out _) &&
        GoalFeedbackEngine.Valid(r.Feedback) && r.Feedback.Scope == "Section";
    public void Save(TrackSection section)
    {
        TrackSectionEngine.Validate(section);
        EnsureNoLinks(_root);
        Directory.CreateDirectory(_root);
        var bytes=JsonSerializer.SerializeToUtf8Bytes(section,new JsonSerializerOptions { WriteIndented=true });
        if(bytes.Length>1024*1024) throw new InvalidDataException("The section is too large.");
        string path=Path.Combine(_root,section.Id+".json"),temp=Path.Combine(_root,Guid.NewGuid().ToString("N")+".tmp");
        try { File.WriteAllBytes(temp,bytes); File.Move(temp,path,overwrite:false); }
        finally { if(File.Exists(temp)) File.Delete(temp); }
    }
    public List<TrackSection> Load(out string issue)
    {
        issue=""; var sections=new List<TrackSection>();
        EnsureNoLinks(_root);
        if(!Directory.Exists(_root)) return sections;
        foreach(var path in Directory.EnumerateFiles(_root,"*.json").Take(501))
        {
            if(sections.Count>=500) { issue="Showing the first 500 sections."; break; }
            try
            {
                var info=new FileInfo(path);
                if(info.Length>1024*1024 || (info.Attributes & FileAttributes.ReparsePoint)!=0) throw new InvalidDataException();
                var s=JsonSerializer.Deserialize<TrackSection>(File.ReadAllBytes(path)) ?? throw new InvalidDataException();
                TrackSectionEngine.Validate(s); sections.Add(s);
            }
            catch(Exception ex) when(ex is IOException or InvalidDataException or JsonException or ArgumentException or NullReferenceException)
            { issue="One or more invalid section files were preserved and skipped."; }
        }
        return sections.OrderByDescending(s=>s.CreatedUtc).ToList();
    }
}
