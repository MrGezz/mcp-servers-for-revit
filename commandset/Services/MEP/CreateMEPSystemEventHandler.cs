using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using RevitMCPCommandSet.Models.MEP;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.MEP
{
  public class CreateMEPSystemEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
  {
    private UIApplication uiApp;
    private UIDocument uiDoc => uiApp.ActiveUIDocument;
    private Document doc => uiDoc.Document;
    private Autodesk.Revit.ApplicationServices.Application app => uiApp.Application;

    private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

    public List<MEPSystemCreationInfo> CreatedInfo { get; private set; }

    public AIResult<List<int>> Result { get; private set; }
    private List<string> _warnings = new List<string>();

    public void SetParameters(List<MEPSystemCreationInfo> data)
    {
      CreatedInfo = data;
      _resetEvent.Reset();
    }

    public void Execute(UIApplication uiapp)
    {
      uiApp = uiapp;

      try
      {
        var systemIds = new List<int>();
        _warnings.Clear();

        foreach (var data in CreatedInfo)
        {
          using (Transaction transaction = new Transaction(doc, "Create MEP System"))
          {
            transaction.Start();

            ElementId systemTypeId = GetSystemTypeId(data.SystemType);
            if (systemTypeId == null || systemTypeId == ElementId.InvalidElementId)
            {
              _warnings.Add($"Unsupported system type: {data.SystemType}");
              transaction.Commit();
              continue;
            }

            MEPSystem system = null;

            // Mechanical systems
            if (data.SystemType.Equals("SupplyAir", StringComparison.OrdinalIgnoreCase) ||
                data.SystemType.Equals("ReturnAir", StringComparison.OrdinalIgnoreCase) ||
                data.SystemType.Equals("ExhaustAir", StringComparison.OrdinalIgnoreCase))
            {
              system = MechanicalSystem.Create(doc, systemTypeId);
            }
            // Plumbing systems
            else if (data.SystemType.Equals("Sanitary", StringComparison.OrdinalIgnoreCase) ||
                     data.SystemType.Equals("HydronicSupply", StringComparison.OrdinalIgnoreCase) ||
                     data.SystemType.Equals("HydronicReturn", StringComparison.OrdinalIgnoreCase))
            {
              system = PipingSystem.Create(doc, systemTypeId);
            }
            else
            {
              _warnings.Add($"Unhandled system type: {data.SystemType}");
            }

            if (system != null)
            {
              if (!string.IsNullOrEmpty(data.Name))
              {
                system.Name = data.Name;
              }

              if (data.ElementIds != null && data.ElementIds.Count > 0)
              {
                List<ElementId> elemIds = data.ElementIds.Select(id => ElementIdFactory.Create(id)).ToList();
                VersionCompat.AddElementsToMEPSystem(doc, system, elemIds);
              }

              systemIds.Add(system.Id.GetIntValue());
            }

            transaction.Commit();
          }
        }

        string message = $"Successfully created {systemIds.Count} MEP system(s).";
        if (_warnings.Count > 0)
        {
          message += "\n\nWarnings:\n  - " + string.Join("\n  - ", _warnings);
        }
        Result = new AIResult<List<int>>
        {
          Success = true,
          Message = message,
          Response = systemIds,
        };
      }
      catch (Exception ex)
      {
        Result = new AIResult<List<int>>
        {
          Success = false,
          Message = $"Error creating MEP system: {ex.Message}",
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

    private ElementId GetSystemTypeId(string systemType)
    {
      // MechanicalSystemType and PipingSystemType are present and usable on Revit
      // 2022-2027 without any version guard.  BuiltInCategory.OST_MEPSystems and
      // OST_PipingSystems do not exist in any Revit version and were never valid
      // enum values; they were invented by pattern-matching rather than looked up.
      switch (systemType.ToLower())
      {
        case "supplyair":
        case "returnair":
        case "exhaustair":
        {
          var mechType = new FilteredElementCollector(doc)
              .OfClass(typeof(MechanicalSystemType))
              .Cast<MechanicalSystemType>()
              .FirstOrDefault(st => st.Name.Equals(systemType, StringComparison.OrdinalIgnoreCase));
          if (mechType != null)
            return mechType.Id;
          // Fallback: first available mechanical system type
          var firstMech = new FilteredElementCollector(doc)
              .OfClass(typeof(MechanicalSystemType))
              .Cast<MechanicalSystemType>()
              .FirstOrDefault();
          return firstMech?.Id ?? ElementId.InvalidElementId;
        }
        case "sanitary":
        case "hydronicsupply":
        case "hydronicreturn":
        {
          var pipeType = new FilteredElementCollector(doc)
              .OfClass(typeof(PipingSystemType))
              .Cast<PipingSystemType>()
              .FirstOrDefault(st => st.Name.Equals(systemType, StringComparison.OrdinalIgnoreCase));
          if (pipeType != null)
            return pipeType.Id;
          // Fallback: first available piping system type
          var firstPipe = new FilteredElementCollector(doc)
              .OfClass(typeof(PipingSystemType))
              .Cast<PipingSystemType>()
              .FirstOrDefault();
          return firstPipe?.Id ?? ElementId.InvalidElementId;
        }
        default:
          return ElementId.InvalidElementId;
      }
    }

    public bool WaitForCompletion(int timeoutMilliseconds = 15000)
    {
      _resetEvent.Reset();
      return _resetEvent.WaitOne(timeoutMilliseconds);
    }

    public string GetName()
    {
      return "Create MEP System";
    }
  }
}
