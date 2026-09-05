namespace Moirai.Ipc;

// Whether a plugin whose internal name contains the given text is loaded, rechecked once a
// second: the snapshot asks every frame and the installed-plugin list is not free to walk.
public sealed class LoadedPluginCheck(string internalNamePart)
{
    private const long RecheckMs = 1000;
    private bool _loaded;
    private long _checkedMs = long.MinValue / 2;

    public bool Loaded
    {
        get
        {
            var now = Environment.TickCount64;
            if (now - _checkedMs >= RecheckMs)
            {
                _checkedMs = now;
                _loaded = Svc.PluginInterface.InstalledPlugins.Any(p =>
                    p.IsLoaded && p.InternalName.Contains(internalNamePart, StringComparison.OrdinalIgnoreCase));
            }
            return _loaded;
        }
    }
}
