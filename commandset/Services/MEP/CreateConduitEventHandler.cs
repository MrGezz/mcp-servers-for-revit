using Autodesk.Revit.DB.Electrical;
using RevitMCPCommandSet.Utils;
using RevitMCPCommandSet.Models.MEP;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.MEP
{
  public class CreateConduitEventHandler : WaitableEventHandlerBase, IExternalEventHandler, IWaitableExternalEventHandler
  {
    private UIApplication uiApp;
    private UIDocument uiDoc => uiApp.ActiveUIDocument;
    private Document doc => uiDoc.Document;
    private Autodesk.Revit.ApplicationServices.Application app => uiApp.Application;


    public List<ConduitCreationInfo> CreatedInfo { get; private set; }

    public AIResult<List<int>> Result { get; private set; }
    private List<string> _warnings = new List<string>();

    public void SetParameters(List<ConduitCreationInfo> data)
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
          {
            _warnings.Add($"No level found near elevation {data.BaseLevel} mm. Conduit skipped.");
            continue;
          }
          double baseOffset = (data.BaseOffset + data.BaseLevel) / 304.8 - baseLevel.Elevation;

          ConduitType conduitType = null;
          if (data.TypeId != -1 && data.TypeId != 0)
          {
            ElementId typeEleId = ElementIdFactory.Create(data.TypeId);
            if (typeEleId != null)
            {
              Element typeEle = doc.GetElement(typeEleId);
              if (typeEle != null && typeEle is ConduitType)
              {
                conduitType = typeEle as ConduitType;
              }
            }
          }

          if (conduitType == null)
          {
            using (var fec = new FilteredElementCollector(doc))
            {
              var allConduitTypes = fec
                  .OfClass(typeof(ConduitType))
                  .Cast<ConduitType>()
                  .ToList();

              if (!string.IsNullOrEmpty(data.ConduitType))
                conduitType = allConduitTypes.FirstOrDefault(ct => ct.Name.Equals(data.ConduitType, StringComparison.OrdinalIgnoreCase));

              if (conduitType == null)
                conduitType = allConduitTypes.FirstOrDefault();
            }

            if (conduitType == null)
            {
              _warnings.Add("No conduit types available in project.");
              continue;
            }
            if (requestedTypeId != -1 && requestedTypeId != 0)
              _warnings.Add($"Requested conduit typeId {requestedTypeId} not found. Defaulted to '{conduitType.Name}' (ID: {conduitType.Id.GetValue()})");
            else if (!string.IsNullOrEmpty(data.ConduitType))
              _warnings.Add($"Conduit type name '{data.ConduitType}' not found. Defaulted to '{conduitType.Name}' (ID: {conduitType.Id.GetValue()})");
          }

          using (Transaction transaction = new Transaction(doc, "Create Conduit"))
          {
            transaction.Start();

            Conduit conduit = Conduit.Create(
                doc,
                conduitType.Id,
                JZPoint.ToXYZ(data.StartPoint),
                JZPoint.ToXYZ(data.EndPoint),
                baseLevel.Id
            );

            if (conduit != null)
            {
              Parameter offsetParam = conduit.get_Parameter(BuiltInParameter.RBS_OFFSET_PARAM);
              if (offsetParam != null)
                offsetParam.Set(baseOffset);

              Parameter diamParam = conduit.get_Parameter(BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM);
              if (diamParam != null)
                diamParam.Set(data.Diameter / 304.8);

              elementIds.Add(conduit.Id.GetIntValue());
            }

            transaction.Commit();
          }
        }

        bool created = elementIds.Count > 0;
        string message = created
            ? $"Successfully created {elementIds.Count} conduit(s)."
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
          Message = $"Error creating conduit: {ex.Message}",
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
      return "Create Conduit";
    }
  }
}
