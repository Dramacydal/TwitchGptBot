using OpenRouter.NET.Models;

namespace TwitchGpt.Gpt.Entities;

public class HistoryHolder
{
    private List<Message> _history = new();

    private readonly Dictionary<Message, string> _contentTagDictionary = new();
    private readonly List<Tuple<Message, string>> _contentTagList = new();

    private readonly Lock _lock = new();

    private void ExecuteLocked(Action a)
    {
        using var _ = _lock.EnterScope();

        a();
    }
    
    public void AddEntries(IEnumerable<Message> entries, string tag = "")
    {
        using var _ = _lock.EnterScope();
        
        foreach (var entry in entries)
        {
            _history.Add(entry);
            if (!string.IsNullOrEmpty(tag))
            {
                _contentTagDictionary.Add(entry, tag);
                _contentTagList.Add(new(entry, tag));
            }
        }
    }

    public List<Message> GetEntries() => _history;

    public List<Message> CopyEntries()
    {
        using var _ = _lock.EnterScope();

        List<Message> entries = new();
        foreach (var entry in _history)
            entries.Add(entry);

        return entries;
    }

    public int Count()
    {
        using var _ = _lock.EnterScope();

        return _history.Count / 2;
    }

    public void Reset()
    {
        using var _ = _lock.EnterScope();
        _history.Clear();
    }

    public void Set(List<Message> history)
    {
        _history = history;
    }

    public int CountByTag(string tag)
    {
        using var _ = _lock.EnterScope();
        return _contentTagList.Count(x => x.Item2 == tag);
    }

    public void RemoveEntriesWithContentTag(string tag, int count)
    {
        using var _ = _lock.EnterScope();

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
    }

    private void RemoveContentTags(Message content)
    {
        _contentTagList.RemoveAll(e => e.Item1 == content);
        _contentTagDictionary.Remove(content);
    }
}
