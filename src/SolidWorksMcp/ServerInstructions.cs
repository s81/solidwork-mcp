namespace SolidWorksMcp;

internal static class ServerInstructions
{
    public const string Text = """
        Tools for automating a local SolidWorks (2022 or newer) installation.
        Conventions:
        - Lengths are millimetres and angles are degrees unless a parameter says otherwise; mass is kg.
        - 'document' parameters accept a full path, a file name or a window title; omit to use the active document.
        - Sketch coordinates are 2D in the sketch plane, relative to the part origin.
        - Planes may be given as front/top/right or by feature name.
        - Call sw_connect first if unsure SolidWorks is running. Changes are not saved until sw_save_document.
        - Revolve needs a closed profile plus one centerline in the same sketch. For fillets and chamfers, use sw_list_edges and pass edge midpoints as points.
        - Prefer sw_list_features / sw_list_dimensions before editing so you use real feature and dimension names.
        """;
}
