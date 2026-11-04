using ModelContextProtocol;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace SolidWorksMcp.SolidWorks;

public sealed record DocumentInfo(
    string Title,
    string Path,
    string Type,
    bool IsActive,
    bool IsDirty,
    bool IsReadOnly,
    bool IsVisible,
    string? ActiveConfiguration);

/// <summary>Document lookup and description helpers. Call only from the STA thread.</summary>
public static class Documents
{
    public static IEnumerable<ModelDoc2> Enumerate(SldWorks app)
    {
        if (app.GetDocuments() is not object[] docs)
        {
            yield break;
        }
        foreach (object d in docs)
        {
            if (d is ModelDoc2 m)
            {
                yield return m;
            }
        }
    }

    /// <summary>Find an open document by full path, file name or window title; null/empty means the active document.</summary>
    public static ModelDoc2 Resolve(SldWorks app, string? document)
    {
        if (string.IsNullOrWhiteSpace(document))
        {
            return app.ActiveDoc as ModelDoc2
                ?? throw new McpException("No document is active in SolidWorks. Open one with sw_open_document or pass 'document'.");
        }

        string wanted = document.Trim().Trim('"');
        if (app.GetOpenDocumentByName(wanted) is ModelDoc2 direct)
        {
            return direct;
        }

        foreach (ModelDoc2 m in Enumerate(app))
        {
            string path = m.GetPathName() ?? string.Empty;
            string title = m.GetTitle() ?? string.Empty;
            if (Matches(wanted, path, title))
            {
                return m;
            }
        }

        throw new McpException($"Document '{document}' is not open. Use sw_list_open_documents to see open documents, or sw_open_document to open it.");
    }

    internal static bool Matches(string wanted, string path, string title)
    {
        const StringComparison c = StringComparison.OrdinalIgnoreCase;
        if (path.Length > 0 && (string.Equals(path, wanted, c) || string.Equals(System.IO.Path.GetFileName(path), wanted, c)
            || string.Equals(System.IO.Path.GetFileNameWithoutExtension(path), wanted, c)))
        {
            return true;
        }
        // Titles can be "Part1", "Part1.SLDPRT" or "Drawing1 - Sheet1" depending on user settings.
        return string.Equals(title, wanted, c)
            || string.Equals(System.IO.Path.GetFileNameWithoutExtension(title), wanted, c)
            || title.StartsWith(wanted + " - ", c);
    }

    public static DocumentInfo Describe(SldWorks app, ModelDoc2 m)
    {
        var active = app.ActiveDoc as ModelDoc2;
        string? config = null;
        if (m.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
        {
            config = (m.ConfigurationManager?.ActiveConfiguration as Configuration)?.Name;
        }
        return new DocumentInfo(
            Title: m.GetTitle(),
            Path: m.GetPathName() ?? string.Empty,
            Type: TypeName(m.GetType()),
            IsActive: active is not null && string.Equals(active.GetPathName(), m.GetPathName(), StringComparison.OrdinalIgnoreCase)
                && active.GetTitle() == m.GetTitle(),
            IsDirty: m.GetSaveFlag(),
            IsReadOnly: m.IsOpenedReadOnly(),
            IsVisible: m.Visible,
            ActiveConfiguration: config);
    }

    public static string TypeName(int type) => (swDocumentTypes_e)type switch
    {
        swDocumentTypes_e.swDocPART => "part",
        swDocumentTypes_e.swDocASSEMBLY => "assembly",
        swDocumentTypes_e.swDocDRAWING => "drawing",
        _ => "unknown",
    };

    public static swDocumentTypes_e TypeFromExtension(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".sldprt" or ".prtdot" => swDocumentTypes_e.swDocPART,
        ".sldasm" or ".asmdot" => swDocumentTypes_e.swDocASSEMBLY,
        ".slddrw" or ".drwdot" => swDocumentTypes_e.swDocDRAWING,
        _ => swDocumentTypes_e.swDocNONE,
    };

    public static void RequireType(ModelDoc2 m, swDocumentTypes_e type, string toolPurpose)
    {
        if (m.GetType() != (int)type)
        {
            throw new McpException($"{toolPurpose} requires a {TypeName((int)type)} document, but '{m.GetTitle()}' is a {TypeName(m.GetType())}.");
        }
    }

    /// <summary>Open (or return already-open) document silently. Returns the doc and whether this call opened it.</summary>
    public static (ModelDoc2 Doc, bool OpenedNow) OpenSilently(SldWorks app, string fullPath, bool readOnly, bool visible, string? configuration = null)
    {
        if (app.GetOpenDocumentByName(fullPath) is ModelDoc2 existing)
        {
            return (existing, false);
        }

        swDocumentTypes_e type = TypeFromExtension(fullPath);
        if (type == swDocumentTypes_e.swDocNONE)
        {
            throw new McpException($"'{fullPath}' is not a SolidWorks part, assembly or drawing (.sldprt/.sldasm/.slddrw).");
        }

        int options = (int)swOpenDocOptions_e.swOpenDocOptions_Silent;
        if (readOnly)
        {
            options |= (int)swOpenDocOptions_e.swOpenDocOptions_ReadOnly;
        }

        int errors = 0, warnings = 0;
        bool restoreVisibility = false;
        if (!visible)
        {
            app.DocumentVisible(false, (int)type);
            restoreVisibility = true;
        }
        ModelDoc2? doc;
        try
        {
            doc = app.OpenDoc6(fullPath, (int)type, options, configuration ?? string.Empty, ref errors, ref warnings);
        }
        finally
        {
            if (restoreVisibility)
            {
                app.DocumentVisible(true, (int)type);
            }
        }

        if (doc is null)
        {
            throw new McpException($"SolidWorks could not open '{fullPath}': {SwFlags.Describe<swFileLoadError_e>(errors)}" +
                (warnings != 0 ? $" (warnings: {SwFlags.Describe<swFileLoadWarning_e>(warnings)})" : string.Empty));
        }
        return (doc, true);
    }

    public static void Close(SldWorks app, ModelDoc2 doc) => app.CloseDoc(doc.GetTitle());
}
