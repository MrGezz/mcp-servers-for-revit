using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Views
{
    public class CreateSectionViewEventHandler : WaitableEventHandlerBase, IExternalEventHandler, IWaitableExternalEventHandler
    {
        private UIApplication uiApp;
        private UIDocument uiDoc => uiApp.ActiveUIDocument;
        private Document doc => uiDoc.Document;


        public string ViewName { get; private set; }
        public double MinX { get; private set; }
        public double MinY { get; private set; }
        public double MinZ { get; private set; }
        public double MaxX { get; private set; }
        public double MaxY { get; private set; }
        public double MaxZ { get; private set; }
        public string ViewFamilyTypeName { get; private set; }

        public AIResult<int> Result { get; private set; }

        public void SetParameters(string name, double minX, double minY, double minZ, double maxX, double maxY, double maxZ, string viewFamilyTypeName)
        {
            ViewName = name;
            MinX = minX;
            MinY = minY;
            MinZ = minZ;
            MaxX = maxX;
            MaxY = maxY;
            MaxZ = maxZ;
            ViewFamilyTypeName = viewFamilyTypeName;
            _resetEvent.Reset();
        }

        public void Execute(UIApplication uiapp)
        {
            uiApp = uiapp;

            try
            {
                using (Transaction trans = new Transaction(doc, "Create Section View"))
                {
                    trans.Start();

                    ViewFamilyType vft;
                    using (var collector = new FilteredElementCollector(doc))
                    {
                        vft = collector
                            .OfClass(typeof(ViewFamilyType))
                            .Cast<ViewFamilyType>()
                            .FirstOrDefault(vftype =>
                                vftype.ViewFamily == ViewFamily.Section &&
                                (string.IsNullOrEmpty(ViewFamilyTypeName) || vftype.Name == ViewFamilyTypeName));
                    }

                    if (vft == null)
                    {
                        using (var collector = new FilteredElementCollector(doc))
                        {
                            vft = collector
                                .OfClass(typeof(ViewFamilyType))
                                .Cast<ViewFamilyType>()
                                .FirstOrDefault(vftype => vftype.ViewFamily == ViewFamily.Section);
                        }
                    }

                    if (vft == null)
                    {
                        Result = new AIResult<int> { Success = false, Message = "No section view family type found" };
                        return;
                    }

                    XYZ minPt = new XYZ(MinX / 304.8, MinY / 304.8, MinZ / 304.8);
                    XYZ maxPt = new XYZ(MaxX / 304.8, MaxY / 304.8, MaxZ / 304.8);

                    BoundingBoxXYZ sectionBox = new BoundingBoxXYZ
                    {
                        Min = minPt,
                        Max = maxPt
                    };

                    ViewSection sectionView = ViewSection.CreateSection(doc, vft.Id, sectionBox);

                    if (!string.IsNullOrEmpty(ViewName))
                    {
                        sectionView.Name = ViewName;
                    }

                    int viewId = sectionView.Id.GetIntValue();

                    trans.Commit();

                    Result = new AIResult<int>
                    {
                        Success = true,
                        Message = $"Section view '{sectionView.Name}' created successfully",
                        Response = viewId
                    };
                }
            }
            catch (Exception ex)
            {
                Result = new AIResult<int>
                {
                    Success = false,
                    Message = $"Error creating section view: {ex.Message}"
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

        public string GetName() => "Create Section View";
    }
}
