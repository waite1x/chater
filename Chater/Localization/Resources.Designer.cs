#nullable enable

using System.CodeDom.Compiler;
using System.Globalization;
using System.Resources;

namespace Chater.Localization;

[GeneratedCode("ResXFileCodeGenerator", "1.0.0.0")]
public static class Resources
{
    private static ResourceManager? _resourceManager;

    public static ResourceManager ResourceManager => _resourceManager ??= new("Chater.Localization.Resources", typeof(Resources).Assembly);

    public static CultureInfo? Culture { get; set; }
}
