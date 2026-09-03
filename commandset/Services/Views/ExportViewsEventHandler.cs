using RevitMCPCommandSet.Utils;
using RevitMCPCommandSet.Models.Views;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Views
{
  public class ExportViewsEventHandler : WaitableEventHandlerBase, IExternalEventHandler, IWaitableExternalEventHandler
  {
    private UIApplication uiApp;
    private UIDocument uiDoc => uiApp.ActiveUIDocument;
    private Document doc => uiDoc.Document;
    private Autodesk.Revit.ApplicationServices.Application app => uiApp.Application;


    public List<ExportSettingsInfo> ExportInfo { get; private set; }

    public AIResult<List<string>> Result { get; private set; }
    private List<string> _warnings = new List<string>();

    public void SetParameters(List<ExportSettingsInfo> data)
    {
      ExportInfo = data;
      _resetEvent.Reset();
    }

    public void Execute(UIApplication uiapp)
    {
      uiApp = uiapp;

      try
      {
        var exportedFiles = new List<string>();
        _warnings.Clear();

        foreach (var data in ExportInfo)
        {
          if (data.ViewIds == null || data.ViewIds.Count == 0)
          {
            _warnings.Add("No view IDs provided for export.");
            continue;
          }

          // IFC has no view scope (see the IFC case below), so it runs once per request.
          bool ifcExported = false;

          foreach (int viewId in data.ViewIds)
          {
            ElementId elemId = ElementIdFactory.Create(viewId);
            View view = doc.GetElement(elemId) as View;

            if (view == null)
            {
              _warnings.Add($"View with ID {viewId} not found.");
              continue;
            }

            string folderPath = data.FolderPath;
            string fileName = data.FileName;

            if (string.IsNullOrEmpty(fileName))
              fileName = view.Name;

            switch (data.Format.ToUpper())
            {
              case "PNG":
              case "JPG":
              {
                ImageExportOptions imgOpts = new ImageExportOptions();
                imgOpts.FilePath = System.IO.Path.Combine(folderPath, fileName);
                imgOpts.ZoomType = ZoomFitType.FitToPage;
                imgOpts.PixelSize = 1024;
                imgOpts.ImageResolution = ImageResolution.DPI_150;
                imgOpts.ExportRange = ExportRange.SetOfViews;
                imgOpts.SetViewsAndSheets(new List<ElementId> { elemId });
                imgOpts.HLRandWFViewsFileType = data.Format.ToUpper() == "PNG"
                    ? ImageFileType.PNG
                    : ImageFileType.JPEGLossless;
                imgOpts.ShadowViewsFileType = imgOpts.HLRandWFViewsFileType;

                // Revit appends its own suffix to the file name, so the written file is
                // found by globbing. Snapshot the folder FIRST: with several views sharing
                // one fileName, a glob taken after each export returned every file written
                // so far in the batch, so view 1 was listed N times and view 2 N-1 times.
                string imgPattern = $"{fileName}*.{data.Format.ToLower()}";
                var before = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
                if (System.IO.Directory.Exists(folderPath))
                  foreach (var f in System.IO.Directory.GetFiles(folderPath, imgPattern))
                    before[f] = System.IO.File.GetLastWriteTimeUtc(f);
                doc.ExportImage(imgOpts);
                foreach (var f in System.IO.Directory.GetFiles(folderPath, imgPattern))
                {
                  DateTime was;
                  bool isNew = !before.TryGetValue(f, out was) || System.IO.File.GetLastWriteTimeUtc(f) != was;
                  if (isNew) exportedFiles.Add(System.IO.Path.GetFileName(f));
                }
                break;
              }
              case "DWG":
              case "DXF":
              {
                DWGExportOptions dwgOpts = new DWGExportOptions();

                ICollection<ElementId> viewIds = new List<ElementId> { elemId };
                doc.Export(folderPath, fileName, viewIds, dwgOpts);
                exportedFiles.Add($"{fileName}.{data.Format.ToLower()}");
                break;
              }
              case "IFC":
              {
                // Two defects were fixed here.
                //
                // 1. IFCExportOptions is present on Revit 2022-2027 with a parameterless
                //    constructor, so the "not supported in Revit 2026" branch was an
                //    invented limitation. Worse, the exportedFiles.Add below it sat
                //    OUTSIDE the #endif, so on 2026 the tool reported a file it had
                //    never written. A false success is worse than a refusal.
                //
                // 2. doc.Export(folder, name, IFCExportOptions) exports the WHOLE MODEL.
                //    It takes no view scope, so running it once per requested view wrote
                //    the same full-model file N times and reported N exports. IFC is
                //    therefore done ONCE per request, and the caller is told that the
                //    result is model-scoped rather than view-scoped.
                if (ifcExported)
                {
                  break;
                }

                IFCExportOptions ifcOpts = new IFCExportOptions();
                doc.Export(folderPath, fileName, ifcOpts);
                ifcExported = true;
                exportedFiles.Add($"{fileName}.ifc");
                _warnings.Add(
                  "IFC export is model-scoped: Revit's IFC exporter takes no view selection, so one " +
                  "file containing the whole model was written rather than one file per requested view.");
                break;
              }
              case "DGN":
              {
                DGNExportOptions dgnOpts = new DGNExportOptions();

                ICollection<ElementId> viewIds = new List<ElementId> { elemId };
                doc.Export(folderPath, fileName, viewIds, dgnOpts);
                exportedFiles.Add($"{fileName}.dgn");
                break;
              }
              default:
                _warnings.Add($"Unsupported export format: {data.Format}");
                break;
            }
          }
        }

        if (exportedFiles.Count == 0)
        {
          string errMsg = "No files were exported.";
          if (_warnings.Count > 0)
            errMsg += "\n\nWarnings:\n  - " + string.Join("\n  - ", _warnings);
          Result = new AIResult<List<string>>
          {
            Success = false,
            Message = errMsg,
            Response = exportedFiles,
          };
        }
        else
        {
          string message = $"Successfully exported {exportedFiles.Count} file(s).";
          if (_warnings.Count > 0)
            message += "\n\nWarnings:\n  - " + string.Join("\n  - ", _warnings);
          Result = new AIResult<List<string>>
          {
            Success = true,
            Message = message,
            Response = exportedFiles,
          };
        }
      }
      catch (Exception ex)
      {
        Result = new AIResult<List<string>>
        {
          Success = false,
          Message = $"Error exporting views: {ex.Message}",
        };
        // (dialog removed: a modal TaskDialog here blocks the shared ExternalEvent
        //  queue for every other command. The message already reaches the caller
        //  through the result set just below/above.)
      }
      finally
      {
        _resetEvent.Set();
      }
    }

    public bool WaitForCompletion(int timeoutMilliseconds = 60000)
    {
      return _resetEvent.WaitOne(timeoutMilliseconds);
    }

    public string GetName()
    {
      return "Export Views";
    }
  }
}
