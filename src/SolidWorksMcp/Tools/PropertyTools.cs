using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorksMcp.Infrastructure;
using SolidWorksMcp.SolidWorks;

namespace SolidWorksMcp.Tools;

[McpServerToolType]
public sealed class PropertyTools(SolidWorksSession sw)
{
    public sealed record CustomProperty(string Name, string Value, string ResolvedValue, string Type, string Scope);

    [McpServerTool(Name = "sw_get_custom_properties", Title = "Get custom properties", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Read custom properties (e.g. PartNo, Description, Material, Finish, Weight). Returns file-level properties and, for parts/assemblies, those of the configuration. ResolvedValue has expressions such as \"SW-Mass@Part1.SLDPRT\" evaluated.")]
    public Task<IReadOnlyList<CustomProperty>> GetCustomProperties(
        [Description("Path, file name or title. Omit for the active document.")] string? document = null,
        [Description("Configuration name. Omit for the active configuration; pass \"\" for file-level only.")] string? configuration = null,
        CancellationToken ct = default)
        => sw.RunOnDocumentAsync<IReadOnlyList<CustomProperty>>(document, (app, doc) =>
        {
            var result = new List<CustomProperty>();
            result.AddRange(Read(doc, string.Empty, "file"));
            string config = ResolveConfigName(doc, configuration);
            if (config.Length > 0)
            {
                result.AddRange(Read(doc, config, $"configuration:{config}"));
            }
            return result;
        }, ct);

    internal static string ResolveConfigName(ModelDoc2 doc, string? configuration)
    {
        if (doc.GetType() == (int)swDocumentTypes_e.swDocDRAWING)
        {
            return string.Empty;
        }
        if (configuration is null)
        {
            return (doc.ConfigurationManager.ActiveConfiguration as Configuration)?.Name ?? string.Empty;
        }
        if (configuration.Length > 0 && doc.GetConfigurationByName(configuration) is null)
        {
            throw new McpException($"Configuration '{configuration}' not found. Use sw_list_configurations.");
        }
        return configuration;
    }

    internal static IEnumerable<CustomProperty> Read(ModelDoc2 doc, string config, string scope)
    {
        CustomPropertyManager mgr = doc.Extension.CustomPropertyManager[config];
        if (mgr?.GetNames() is not object[] names)
        {
            yield break;
        }
        foreach (string name in names.Cast<string>())
        {
            mgr.Get6(name, false, out string value, out string resolved, out _, out _);
            int type = mgr.GetType2(name);
            yield return new CustomProperty(name, value ?? string.Empty, resolved ?? string.Empty, ((swCustomInfoType_e)type).ToString().Replace("swCustomInfo", string.Empty), scope);
        }
    }

    internal static string? ReadResolved(ModelDoc2 doc, string config, string name)
    {
        foreach (string scope in new[] { config, string.Empty }.Distinct())
        {
            CustomPropertyManager mgr = doc.Extension.CustomPropertyManager[scope];
            if (mgr is null) continue;
            int status = mgr.Get6(name, false, out _, out string resolved, out _, out _);
            if (status != (int)swCustomInfoGetResult_e.swCustomInfoGetResult_NotPresent && resolved is not null)
            {
                return resolved;
            }
        }
        return null;
    }

    [McpServerTool(Name = "sw_set_custom_property", Title = "Set custom property", Idempotent = true, OpenWorld = false)]
    [Description("Create or overwrite a custom property. Value may be text or a link expression such as \"SW-Mass@@Default@Part1.SLDPRT\". Save the document afterwards to persist.")]
    public Task<string> SetCustomProperty(
        [Description("Property name, e.g. PartNo.")] string name,
        [Description("Property value or expression.")] string value,
        [Description("Path, file name or title. Omit for the active document.")] string? document = null,
        [Description("Configuration name for a configuration-specific property. Omit or \"\" for file-level.")] string? configuration = "",
        [Description("text | number | date | yesno")] string type = "text",
        CancellationToken ct = default)
        => sw.RunOnDocumentAsync(document, (app, doc) =>
        {
            string config = configuration is null || configuration.Length == 0 ? string.Empty : ResolveConfigName(doc, configuration);
            var infoType = type.ToLowerInvariant() switch
            {
                "text" => swCustomInfoType_e.swCustomInfoText,
                "number" => swCustomInfoType_e.swCustomInfoNumber,
                "double" => swCustomInfoType_e.swCustomInfoDouble,
                "date" => swCustomInfoType_e.swCustomInfoDate,
                "yesno" => swCustomInfoType_e.swCustomInfoYesOrNo,
                _ => throw new McpException($"Unknown property type '{type}'."),
            };
            CustomPropertyManager mgr = doc.Extension.CustomPropertyManager[config];
            int r = mgr.Add3(name, (int)infoType, value, (int)swCustomPropertyAddOption_e.swCustomPropertyReplaceValue);
            if (r != (int)swCustomInfoAddResult_e.swCustomInfoAddResult_AddedOrChanged)
            {
                throw new McpException($"Could not set '{name}': {(swCustomInfoAddResult_e)r}");
            }
            mgr.Get6(name, false, out _, out string resolved, out _, out _);
            return $"{name} = {value} (resolved: {resolved}) on {(config.Length == 0 ? "file" : config)} of {doc.GetTitle()}";
        }, ct);

    [McpServerTool(Name = "sw_delete_custom_property", Title = "Delete custom property", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Delete a custom property.")]
    public Task<string> DeleteCustomProperty(
        [Description("Property name.")] string name,
        [Description("Path, file name or title. Omit for the active document.")] string? document = null,
        [Description("Configuration name, or omit/\"\" for file-level.")] string? configuration = "",
        CancellationToken ct = default)
        => sw.RunOnDocumentAsync(document, (app, doc) =>
        {
            string config = string.IsNullOrEmpty(configuration) ? string.Empty : ResolveConfigName(doc, configuration);
            int r = doc.Extension.CustomPropertyManager[config].Delete2(name);
            return r == (int)swCustomInfoDeleteResult_e.swCustomInfoDeleteResult_OK
                ? $"Deleted {name}"
                : throw new McpException($"Could not delete '{name}': {(swCustomInfoDeleteResult_e)r}");
        }, ct);

    public sealed record MassInfo(double MassKg, double VolumeMm3, double SurfaceAreaMm2, double DensityKgPerM3, double[] CenterOfMassMm, string Configuration);

    [McpServerTool(Name = "sw_get_mass_properties", Title = "Mass properties", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Mass (kg), volume (mm³), surface area (mm²), density and centre of mass (mm) of a part or assembly in its active configuration. Useful for weight estimates, shipping and costing.")]
    public Task<MassInfo> GetMassProperties(
        [Description("Path, file name or title. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
        => sw.RunOnDocumentAsync(document, (app, doc) =>
        {
            if (doc.GetType() == (int)swDocumentTypes_e.swDocDRAWING)
            {
                throw new McpException("Mass properties need a part or assembly.");
            }
            var mp = doc.Extension.CreateMassProperty2() as MassProperty2
                ?? throw new McpException("SolidWorks could not compute mass properties (no solid bodies?).");
            mp.UseSystemUnits = true;
            var com = mp.CenterOfMass as double[];
            return new MassInfo(
                MassKg: Units.Round(mp.Mass),
                VolumeMm3: Units.Round(mp.Volume * 1e9, 3),
                SurfaceAreaMm2: Units.Round(mp.SurfaceArea * 1e6, 3),
                DensityKgPerM3: Units.Round(mp.Density, 3),
                CenterOfMassMm: Units.MToMm(com),
                Configuration: (doc.ConfigurationManager.ActiveConfiguration as Configuration)?.Name ?? string.Empty);
        }, ct);

    [McpServerTool(Name = "sw_get_material", Title = "Get material", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Get the material assigned to a part (and its material database).")]
    public Task<object> GetMaterial(
        [Description("Path, file name or title. Omit for the active document.")] string? document = null,
        [Description("Configuration. Omit for the active configuration.")] string? configuration = null,
        CancellationToken ct = default)
        => sw.RunOnDocumentAsync<object>(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocPART, "Material lookup");
            string config = ResolveConfigName(doc, configuration);
            string name = ((PartDoc)doc).GetMaterialPropertyName2(config, out string db);
            return new { material = string.IsNullOrEmpty(name) ? null : name, database = db, configuration = config };
        }, ct);

    [McpServerTool(Name = "sw_set_material", Title = "Set material", Idempotent = true, OpenWorld = false)]
    [Description("Assign a material from a material database to a part, e.g. database \"SOLIDWORKS Materials\", material \"AISI 304\" or \"Plain Carbon Steel\".")]
    public Task<object> SetMaterial(
        [Description("Material name exactly as in the material library.")] string material,
        [Description("Database name, e.g. \"SOLIDWORKS Materials\" or the name of a custom .sldmat library.")] string database = "SOLIDWORKS Materials",
        [Description("Path, file name or title. Omit for the active document.")] string? document = null,
        [Description("Configuration. Omit for the active configuration.")] string? configuration = null,
        CancellationToken ct = default)
        => sw.RunOnDocumentAsync<object>(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocPART, "Material assignment");
            string config = ResolveConfigName(doc, configuration);
            var part = (PartDoc)doc;
            part.SetMaterialPropertyName2(config, database, material);
            string applied = part.GetMaterialPropertyName2(config, out string db);
            if (!string.Equals(applied, material, StringComparison.OrdinalIgnoreCase))
            {
                throw new McpException($"Material '{material}' was not found in database '{database}'. Current material: '{applied}'.");
            }
            return new { material = applied, database = db, configuration = config };
        }, ct);

    [McpServerTool(Name = "sw_list_configurations", Title = "List configurations", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("List the configurations of a part or assembly and which one is active.")]
    public Task<object> ListConfigurations(
        [Description("Path, file name or title. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
        => sw.RunOnDocumentAsync<object>(document, (app, doc) =>
        {
            if (doc.GetType() == (int)swDocumentTypes_e.swDocDRAWING)
            {
                throw new McpException("Drawings have no configurations; use sw_list_drawing_sheets.");
            }
            string active = (doc.ConfigurationManager.ActiveConfiguration as Configuration)?.Name ?? string.Empty;
            var names = (doc.GetConfigurationNames() as string[]) ?? Array.Empty<string>();
            return new
            {
                active,
                configurations = names.Select(n =>
                {
                    var c = doc.GetConfigurationByName(n) as Configuration;
                    return new { name = n, description = c?.Description, isDerived = c?.IsDerived() ?? false };
                }).ToList(),
            };
        }, ct);

    [McpServerTool(Name = "sw_activate_configuration", Title = "Activate configuration", Idempotent = true, OpenWorld = false)]
    [Description("Make a configuration active (the model rebuilds in that configuration).")]
    public Task<string> ActivateConfiguration(
        [Description("Configuration name.")] string configuration,
        [Description("Path, file name or title. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
        => sw.RunOnDocumentAsync(document, (app, doc) =>
        {
            if (!doc.ShowConfiguration2(configuration))
            {
                throw new McpException($"Could not activate configuration '{configuration}'. Use sw_list_configurations.");
            }
            return $"Active configuration of {doc.GetTitle()}: {configuration}";
        }, ct);
}
