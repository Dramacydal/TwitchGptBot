using GenerativeAI.Types;
using TwitchGpt.Helpers;

namespace TwitchGpt.Gpt.Entities;

public class HistoryHolder
{
    private List<Content> _history = new();

    private Dictionary<Content, string> _contentTagDictionary = new();
    private List<Tuple<Content, string>> _contentTagList = new();

    private readonly Locker _lock = new();

    private void Lock(Action a)
    {
        using var l = _lock.Acquire();

        a();
    }
    
    private T Lock<T>(Func<T> f)
    {
        using var l = _lock.Acquire();

        return f();
    }

    public void AddEntries(IEnumerable<Content> entries, string tag = "")
    {
        foreach (var entry in entries)
            AddEntry(entry, tag);
    }

    private void AddEntry(Content entry, string tag)
    {
        Lock(() =>
        {
            _history.Add(entry);
            if (!string.IsNullOrEmpty(tag))
            {
                _contentTagDictionary.Add(entry, tag);
                _contentTagList.Add(new(entry, tag));
            }
        });
    }

    public List<Content> GetEntries()
    {
        return Lock(() => _history);
    }

    public List<Content> CopyEntries()
    {
        return Lock(() =>
        {
            List<Content> entries = new();
            foreach (var entry in _history)
                entries.Add(entry);

            return entries;
        });
    }

    public int Count()
    {
        return Lock(() => _history.Count) / 2;
    }

    public void Reset()
    {
        Lock(() => _history.Clear());
    }

    public void Set(List<Content> history)
    {
        Lock(() => _history = history);
    }

    public int CountByTag(string tag)
    {
        return Lock(() => _contentTagList.Count(x => x.Item2 == tag));
    }

    public void RemoveEntriesWithContentTag(string tag, int count)
    {
        Lock(() =>
        {
            var removed = 0;
            var tpls = _contentTagList.Where(e => e.Item2 == tag).ToList();

            foreach (var tpl in tpls)
            {
                _history.Remove(tpl.Item1);
                RemoveContentTags(tpl.Item1);
                
                ++removed;
                if (removed == count * 2)
                    break;
            }
        });
    }

    private void RemoveContentTags(Content content)
    {
        _contentTagList.RemoveAll(e => e.Item1 == content);
        _contentTagDictionary.Remove(content);
    }
}
