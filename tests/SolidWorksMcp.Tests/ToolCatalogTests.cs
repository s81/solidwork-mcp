using System.ComponentModel;
using System.Reflection;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using SolidWorksMcp.SolidWorks;

namespace SolidWorksMcp.Tests;

/// <summary>Guards the quality of the tool surface the LLM sees.</summary>
public class ToolCatalogTests
{
    private static readonly MethodInfo[] Tools = typeof(SolidWorksSession).Assembly.GetTypes()
        .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
        .ToArray();

    [Fact]
    public void HasExpectedToolGroups()
    {
        var names = Tools.Select(m => m.GetCustomAttribute<McpServerToolAttribute>()!.Name).ToHashSet();
        foreach (string expected in new[]
        {
            "sw_connect", "sw_open_document", "sw_save_document", "sw_export", "sw_batch_export", "sw_export_flat_pattern_dxf",
            "sw_get_custom_properties", "sw_set_custom_property", "sw_get_mass_properties", "sw_get_bom",
            "sw_create_sketch", "sw_extrude", "sw_cut_extrude", "sw_set_dimension", "sw_revolve", "sw_fillet", "sw_chamfer", "sw_list_edges", "sw_add_mate", "sw_list_mates", "sw_set_component_fixed", "sw_hole_wizard", "sw_get_sheet_metal_info", "sw_base_flange", "sw_edge_flange", "sw_set_flattened", "sw_set_sheet_metal_parameters", "sw_list_weldment_profiles", "sw_add_structural_member", "sw_get_weldment_cut_list", "sw_trim_extend", "sw_add_end_cap", "sw_add_gusset", "sw_insert_bom_table", "sw_read_bom_tables", "sw_export_bom_table",
            "sw_create_drawing", "sw_list_drawing_sheets", "sw_run_macro", "sw_set_global_variable",
        })
        {
            Assert.Contains(expected, names);
        }
    }

    [Fact]
    public void NamesAreUniqueAndSnakeCase()
    {
        var names = Tools.Select(m => m.GetCustomAttribute<McpServerToolAttribute>()!.Name!).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.All(names, n => Assert.Matches(new Regex("^sw_[a-z0-9_]+$"), n));
    }

    [Fact]
    public void EveryToolAndParameterIsDescribed()
    {
        foreach (var m in Tools)
        {
            Assert.False(string.IsNullOrWhiteSpace(m.GetCustomAttribute<DescriptionAttribute>()?.Description), $"{m.Name} lacks a description");
            foreach (var p in m.GetParameters().Where(p => p.ParameterType != typeof(CancellationToken)))
            {
                Assert.False(string.IsNullOrWhiteSpace(p.GetCustomAttribute<DescriptionAttribute>()?.Description),
                    $"{m.DeclaringType!.Name}.{m.Name}({p.Name}) lacks a description");
            }
        }
    }
}
