using DouDouDeDou.Models;

namespace DouDouDeDou.Utils;

public sealed record ActiveOutput(
    string MappingId,
    string SourceButton,
    InputTarget Target,
    DateTimeOffset PressedAt);

public sealed record SafeReleaseAction(
    InputTarget Target,
    string MappingId,
    string Reason,
    bool ShouldSendPhysicalRelease);

public sealed class SafeReleaseManager
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ActiveOutput> _activeByMappingId = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ActiveOutput> Snapshot()
    {
        lock (_gate)
        {
            return _activeByMappingId.Values
                .Select(x => new ActiveOutput(x.MappingId, x.SourceButton, x.Target.Clone(), x.PressedAt))
                .ToList();
        }
    }

    public bool TryMarkPressed(string mappingId, string sourceButton, InputTarget target, out bool shouldSendPhysicalPress)
    {
        lock (_gate)
        {
            shouldSendPhysicalPress = false;
            if (_activeByMappingId.ContainsKey(mappingId))
            {
                return false;
            }

            var signature = target.Signature;
            var alreadyPressedByAnotherMapping = _activeByMappingId.Values.Any(x => x.Target.Signature == signature);

            _activeByMappingId[mappingId] = new ActiveOutput(mappingId, sourceButton, target.Clone(), DateTimeOffset.Now);
            shouldSendPhysicalPress = !alreadyPressedByAnotherMapping;
            return true;
        }
    }

    public SafeReleaseAction? MarkReleased(string mappingId, string reason)
    {
        lock (_gate)
        {
            if (!_activeByMappingId.Remove(mappingId, out var active))
            {
                return null;
            }

            var hasSameTargetStillActive = _activeByMappingId.Values.Any(x => x.Target.Signature == active.Target.Signature);
            return new SafeReleaseAction(active.Target.Clone(), active.MappingId, reason, !hasSameTargetStillActive);
        }
    }

    public IReadOnlyList<SafeReleaseAction> ReleaseAll(string reason)
    {
        lock (_gate)
        {
            var actions = _activeByMappingId.Values
                .GroupBy(x => x.Target.Signature)
                .Select(group => group.First())
                .Select(x => new SafeReleaseAction(x.Target.Clone(), x.MappingId, reason, true))
                .ToList();

            _activeByMappingId.Clear();
            return actions;
        }
    }
}
