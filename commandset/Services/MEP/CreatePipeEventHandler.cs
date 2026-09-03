using Autodesk.Revit.DB.Plumbing;
using RevitMCPCommandSet.Utils;
using RevitMCPCommandSet.Models.MEP;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.MEP
{
  public class CreatePipeEventHandler : WaitableEventHandlerBase, IExternalEventHandler, IWaitableExternalEventHandler
  {
    private UIApplication uiApp;
    private UIDocument uiDoc => uiApp.ActiveUIDocument;
    private Document doc => uiDoc.Document;
    private Autodesk.Revit.ApplicationServices.Application app => uiApp.Application;


    public List<PipeCreationInfo> CreatedInfo { get; private set; }

    public AIResult<List<int>> Result { get; private set; }
    private List<string> _warnings = new List<string>();

    public void SetParameters(List<PipeCreationInfo> data)
    {
      CreatedInfo = data;
      _resetEvent.Reset();
    }

    public void Execute(UIApplication uiapp)
    {
      uiApp = uiapp;

      try
      {
        var elementIds = new List<int>();
        _warnings.Clear();

        foreach (var data in CreatedInfo)
        {
          int requestedTypeId = data.TypeId;

          Level baseLevel = doc.FindNearestLevel(data.BaseLevel / 304.8);
          if (baseLevel == null)
            continue;
          double baseOffset = (data.BaseOffset + data.BaseLevel) / 304.8 - baseLevel.Elevation;

          PipeType pipeType = null;
          if (data.TypeId != -1 && data.TypeId != 0)
          {
            ElementId typeEleId = ElementIdFactory.Create(data.TypeId);
            if (typeEleId != null)
            {
              Element typeEle = doc.GetElement(typeEleId);
              if (typeEle != null && typeEle is PipeType)
              {
                pipeType = typeEle as PipeType;
              }
            }
          }

          if (pipeType == null)
          {
            using (var fec = new FilteredElementCollector(doc))
            {
              var allPipeTypes = fec.OfClass(typeof(PipeType)).Cast<PipeType>().ToList();

              if (!string.IsNullOrEmpty(data.PipeType))
              {
                pipeType = allPipeTypes.FirstOrDefault(p =>
                    string.Equals(p.Name, data.PipeType, StringComparison.OrdinalIgnoreCase));
              }

              if (pipeType == null)
              {
                pipeType = allPipeTypes.FirstOrDefault();
              }
            }

            if (pipeType == null)
            {
              _warnings.Add("No pipe types available in project.");
              continue;
            }
            if (requestedTypeId != -1 && requestedTypeId != 0)
            {
              _warnings.Add($"Requested pipe typeId {requestedTypeId} not found. Defaulted to '{pipeType.Name}' (ID: {pipeType.Id.GetValue()})");
            }
          }

          using (Transaction transaction = new Transaction(doc, "Create Pipe"))
          {
            transaction.Start();

            MEPSystemType mepSystemType;
            using (var fec = new FilteredElementCollector(doc))
            {
              var allSystemTypes = fec
                  .OfClass(typeof(MEPSystemType))
                  .Cast<MEPSystemType>()
                  .ToList();

              if (!string.IsNullOrWhiteSpace(data.SystemType))
              {
                mepSystemType = allSystemTypes
                    .FirstOrDefault(m => string.Equals(m.Name, data.SystemType, StringComparison.OrdinalIgnoreCase))
                  ?? allSystemTypes
                    .FirstOrDefault(m => m.SystemClassification == MEPSystemClassification.Sanitary);
              }
              else
              {
                mepSystemType = allSystemTypes
                    .FirstOrDefault(m => m.SystemClassification == MEPSystemClassification.Sanitary);
              }
            }

            if (mepSystemType != null)
            {
              Pipe pipe = Pipe.Create(
                  doc,
                  mepSystemType.Id,
                  pipeType.Id,
                  baseLevel.Id,
                  JZPoint.ToXYZ(data.StartPoint),
                  JZPoint.ToXYZ(data.EndPoint)
              );

              if (pipe != null)
              {
                Parameter offsetParam = pipe.get_Parameter(BuiltInParameter.RBS_OFFSET_PARAM);
                if (offsetParam != null)
                  offsetParam.Set(baseOffset);
                Parameter diamParam = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                if (diamParam != null)
                  diamParam.Set(data.Diameter / 304.8);
                elementIds.Add(pipe.Id.GetIntValue());
              }
            }
            else
            {
              _warnings.Add("No matching MEP system type found. Pipe not created.");
            }

            transaction.Commit();
          }
        }

        bool created = elementIds.Count > 0;
        string message = created
            ? $"Successfully created {elementIds.Count} pipe(s)."
            : "Nothing was created.";
        if (_warnings.Count > 0)
        {
          message += "\n\nWarnings:\n  - " + string.Join("\n  - ", _warnings);
        }
        Result = new AIResult<List<int>>
        {
          Success = created,
          Message = message,
          Response = elementIds,
        };
      }
      catch (Exception ex)
      {
        Result = new AIResult<List<int>>
        {
          Success = false,
          Message = $"Error creating pipe: {ex.Message}",
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

    public bool WaitForCompletion(int timeoutMilliseconds = 15000)
    {
      return _resetEvent.WaitOne(timeoutMilliseconds);
    }

    public string GetName()
    {
      return "Create Pipe";
    }
  }
}
