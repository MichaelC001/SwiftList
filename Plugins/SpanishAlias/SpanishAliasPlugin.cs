using SwiftList.PluginSdk.Abstractions.Plugins;

namespace SwiftList.Plugins.SpanishAlias;

public sealed class SpanishAliasPlugin : IPlugin
{
    public string Id => "SwiftList.Plugins.SpanishAlias";
    public string Name => "Spanish Alias";
    public string Version => "1.0.0";
    public string Author => "SwiftList";
}
