using Autodesk.Revit.DB.Mechanical;
using RevitMCPCommandSet.Utils;
using RevitMCPCommandSet.Models.MEP;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.MEP
{
  public class CreateDuctEventHandler : WaitableEventHandlerBase, IExternalEventHandler, IWaitableExternalEventHandler
  {
    private UIApplication uiApp;
    private UIDocument uiDoc => uiApp.ActiveUIDocument;
    private Document doc => uiDoc.Document;
    private Autodesk.Revit.ApplicationServices.Application app => uiApp.Application;


    public List<DuctCreationInfo> CreatedInfo { get; private set; }

    public AIResult<List<int>> Result { get; private set; }
    private List<string> _warnings = new List<string>();

    public void SetParameters(List<DuctCreationInfo> data)
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
            _warnings.Add($"No level found near {data.BaseLevel}mm. Duct skipped.");
            continue;
          }
          double baseOffset = (data.BaseOffset + data.BaseLevel) / 304.8 - baseLevel.Elevation;

          DuctType ductType = null;
          if (data.TypeId != -1 && data.TypeId != 0)
          {
            ElementId typeEleId = ElementIdFactory.Create(data.TypeId);
            if (typeEleId != null)
            {
              Element typeEle = doc.GetElement(typeEleId);
              if (typeEle != null && typeEle is DuctType)
              {
                ductType = typeEle as DuctType;
              }
            }
          }

          if (ductType == null)
          {
            using (var fec = new FilteredElementCollector(doc))
            {
              ductType = fec
                  .OfClass(typeof(DuctType))
                  .Cast<DuctType>()
                  .FirstOrDefault(d => d.Shape == ConnectorProfileType.Rectangular);
            }

            if (ductType == null)
            {
              _warnings.Add("No duct types available in project.");
              continue;
            }
            if (requestedTypeId != -1 && requestedTypeId != 0)
            {
              _warnings.Add($"Requested duct typeId {requestedTypeId} not found. Defaulted to '{ductType.Name}' (ID: {ductType.Id.GetValue()})");
            }
          }

          using (Transaction transaction = new Transaction(doc, "Create Duct"))
          {
            transaction.Start();

            MEPSystemClassification targetClassification;
            switch (data.SystemType?.Trim())
            {
              case "Return Air":
                targetClassification = MEPSystemClassification.ReturnAir;
                break;
              case "Exhaust Air":
                targetClassification = MEPSystemClassification.ExhaustAir;
                break;
              default:
                targetClassification = MEPSystemClassification.SupplyAir;
                break;
            }

            MEPSystemType mepSystemType;
            using (var fec = new FilteredElementCollector(doc))
            {
              mepSystemType = fec
                  .OfClass(typeof(MEPSystemType))
                  .Cast<MEPSystemType>()
                  .FirstOrDefault(m => m.SystemClassification == targetClassification);
            }

            if (mepSystemType != null)
            {
              Duct duct = Duct.Create(
                  doc,
                  mepSystemType.Id,
                  ductType.Id,
                  baseLevel.Id,
                  JZPoint.ToXYZ(data.StartPoint),
                  JZPoint.ToXYZ(data.EndPoint)
              );

              if (duct != null)
              {
                Parameter offsetParam = duct.get_Parameter(BuiltInParameter.RBS_OFFSET_PARAM);
                if (offsetParam != null)
                  offsetParam.Set(baseOffset);

                // Apply cross-section dimensions from caller-supplied width/height (mm -> feet)
                if (ductType.Shape == ConnectorProfileType.Round)
                {
                  Parameter diamParam = duct.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM);
                  if (diamParam != null && data.Width > 0)
                    diamParam.Set(data.Width / 304.8);
                }
                else
                {
                  Parameter widthParam = duct.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM);
                  if (widthParam != null && data.Width > 0)
                    widthParam.Set(data.Width / 304.8);
                  Parameter heightParam = duct.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM);
                  if (heightParam != null && data.Height > 0)
                    heightParam.Set(data.Height / 304.8);
                }

                elementIds.Add(duct.Id.GetIntValue());
              }
            }
            else
            {
              _warnings.Add($"No MEPSystemType with {targetClassification} classification found. Duct not created.");
            }

            transaction.Commit();
          }
        }

        bool created = elementIds.Count > 0;
        string message = created
            ? $"Successfully created {elementIds.Count} duct(s)."
            : "Nothing was created.";
        if (_warnings.Count > 0)
        {
          message += "\n\nWarnings:\n  - " + string.Join("\n  - ", _warnings);
        }
        Result = new AIResult<List<int>>
        {
          Success = elementIds.Count > 0 || CreatedInfo.Count == 0,
          Message = message,
          Response = elementIds,
        };
      }
      catch (Exception ex)
      {
        Result = new AIResult<List<int>>
        {
          Success = false,
          Message = $"Error creating duct: {ex.Message}",
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
      return "Create Duct";
    }
  }
}
