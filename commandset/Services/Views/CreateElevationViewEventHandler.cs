using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Views
{
    public class CreateElevationViewEventHandler : WaitableEventHandlerBase, IExternalEventHandler, IWaitableExternalEventHandler
    {
        private UIApplication uiApp;
        private UIDocument uiDoc => uiApp.ActiveUIDocument;
        private Document doc => uiDoc.Document;


        public string ViewName { get; private set; }
        public int DirectionIndex { get; private set; }
        public string ViewFamilyTypeName { get; private set; }

        public AIResult<int> Result { get; private set; }

        public void SetParameters(string name, int directionIndex, string viewFamilyTypeName)
        {
            ViewName = name;
            DirectionIndex = directionIndex;
            ViewFamilyTypeName = viewFamilyTypeName;
            _resetEvent.Reset();
        }

        public void Execute(UIApplication uiapp)
        {
            uiApp = uiapp;

            try
            {
                using (Transaction trans = new Transaction(doc, "Create Elevation View"))
                {
                    trans.Start();

                    ViewFamilyType vft;
                    using (var collector = new FilteredElementCollector(doc))
                    {
                        vft = collector
                            .OfClass(typeof(ViewFamilyType))
                            .Cast<ViewFamilyType>()
                            .FirstOrDefault(vftype =>
                                vftype.ViewFamily == ViewFamily.Elevation &&
                                (string.IsNullOrEmpty(ViewFamilyTypeName) || vftype.Name == ViewFamilyTypeName));
                    }

                    if (vft == null)
                    {
                        using (var collector = new FilteredElementCollector(doc))
                        {
                            vft = collector
                                .OfClass(typeof(ViewFamilyType))
                                .Cast<ViewFamilyType>()
                                .FirstOrDefault(vftype => vftype.ViewFamily == ViewFamily.Elevation);
                        }
                    }

                    if (vft == null)
                    {
                        Result = new AIResult<int> { Success = false, Message = "No elevation view family type found" };
                        return;
                    }

                    Level level;
                    using (var collector = new FilteredElementCollector(doc))
                    {
                        level = collector
                            .OfClass(typeof(Level))
                            .Cast<Level>()
                            .FirstOrDefault();
                    }

                    if (level == null)
                    {
                        Result = new AIResult<int> { Success = false, Message = "No level found in project" };
                        return;
                    }

                    // CreateElevationMarker(Document, ElementId viewFamilyTypeId, XYZ origin,
                    // int initialViewScale) is the only signature the Revit API has, and it is
                    // identical on 2022-2027. The version ladder that used to stand here guarded
                    // a four-argument Level-id overload that exists in no version.
                    ElevationMarker marker = ElevationMarker.CreateElevationMarker(doc, vft.Id, new XYZ(0, 0, level.Elevation), 100);

                    int dirIndex = Math.Max(0, Math.Min(3, DirectionIndex));
                    ViewSection elevationView = VersionCompat.CreateElevationView(doc, marker, level.Id, dirIndex);

                    if (!string.IsNullOrEmpty(ViewName))
                    {
                        elevationView.Name = ViewName;
                    }

                    int viewId = elevationView.Id.GetIntValue();

                    trans.Commit();

                    Result = new AIResult<int>
                    {
                        Success = true,
                        Message = $"Elevation view '{elevationView.Name}' created successfully at direction index {dirIndex}",
                        Response = viewId
                    };
                }
            }
            catch (Exception ex)
            {
                Result = new AIResult<int>
                {
                    Success = false,
                    Message = $"Error creating elevation view: {ex.Message}"
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public string GetName() => "Create Elevation View";
    }
}
