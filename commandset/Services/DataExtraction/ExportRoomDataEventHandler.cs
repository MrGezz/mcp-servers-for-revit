using Autodesk.Revit.DB.Architecture;
using RevitMCPCommandSet.Utils;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Models.DataExtraction;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.DataExtraction
{
    public class ExportRoomDataEventHandler : WaitableEventHandlerBase, IExternalEventHandler, IWaitableExternalEventHandler
    {
        private bool _includeUnplacedRooms;
        private bool _includeNotEnclosedRooms;

        public AIResult<ExportRoomDataResult> ResultInfo { get; private set; }
        public bool TaskCompleted { get; private set; }

        public void SetParameters(bool includeUnplacedRooms = false, bool includeNotEnclosedRooms = false)
        {
            _includeUnplacedRooms = includeUnplacedRooms;
            _includeNotEnclosedRooms = includeNotEnclosedRooms;
            TaskCompleted = false;
            _resetEvent.Reset();
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
        return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;
                var rooms = new List<RoomDataModel>();
                double totalArea = 0;

                // Collect all rooms in the project
                List<Room> roomCollector;
                using (var coll = new FilteredElementCollector(doc))
                    roomCollector = coll
                        .OfCategory(BuiltInCategory.OST_Rooms)
                        .WhereElementIsNotElementType()
                        .Cast<Room>()
                        .ToList();

                // Revit internal units are feet. Every tool in this set promises
                // metric, so convert here, once, at the source.
                const double FT_TO_MM = 304.8;
                const double FT2_TO_M2 = 0.09290304;
                const double FT3_TO_M3 = 0.028316846592;

                foreach (Room room in roomCollector)
                {
                    // Three states, two of them "zero area". Previously both flags
                    // tested the same condition (Area == 0), so asking for unplaced
                    // rooms alone still returned nothing and reported success.
                    bool unplaced = room.Location == null;
                    bool notEnclosed = !unplaced && room.Area <= 0;
                    if (unplaced && !_includeUnplacedRooms) continue;
                    if (notEnclosed && !_includeNotEnclosedRooms) continue;

                    var roomData = new RoomDataModel
                    {
                        Id = room.Id.GetValue(),
                        UniqueId = room.UniqueId,
                        Name = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "",
                        Number = room.Number ?? "",
                        Level = room.Level?.Name ?? "No Level",
                        Area = Math.Round(room.Area * FT2_TO_M2, 3),
                        Volume = Math.Round(room.Volume * FT3_TO_M3, 3),
                        Perimeter = Math.Round(room.Perimeter * FT_TO_MM, 1),
                        UnboundedHeight = Math.Round(room.UnboundedHeight * FT_TO_MM, 1),
                        Department = room.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT)?.AsString() ?? "",
                        Comments = room.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ?? "",
                        Phase = doc.GetElement(room.get_Parameter(BuiltInParameter.ROOM_PHASE)?.AsElementId())?.Name ?? "",
                        Occupancy = room.get_Parameter(BuiltInParameter.ROOM_OCCUPANCY)?.AsString() ?? "",
                        Status = unplaced ? "unplaced" : notEnclosed ? "not enclosed" : "placed"
                    };

                    rooms.Add(roomData);
                    totalArea += roomData.Area;
                }

                // Two fixes here.
                //
                // 1. Exporting zero rooms is not a success: every room can be dropped by
                //    the unplaced / not-enclosed filters, and the caller was told the
                //    export worked.
                // 2. ExportRoomDataResult carries its OWN success/message properties. Left
                //    unset inside an AIResult envelope they serialise as success:false /
                //    message:null, contradicting the outer Success on every successful
                //    call. They are set to agree.
                bool exported = rooms.Count > 0;
                string exportMessage = exported
                    ? $"Exported {rooms.Count} room(s)."
                    : "No rooms were exported. The document may contain no placed, enclosed rooms, " +
                      "or every room was filtered out.";

                ResultInfo = new AIResult<ExportRoomDataResult>
                {
                    Success = exported,
                    Message = exportMessage,
                    Response = new ExportRoomDataResult
                    {
                        Success = exported,
                        Message = exportMessage,
                        TotalRooms = rooms.Count,
                        TotalArea = totalArea,
                        Rooms = rooms
                    }
                };
            }
            catch (Exception ex)
            {
                ResultInfo = new AIResult<ExportRoomDataResult>
                {
                    Success = false,
                    Message = $"Error exporting room data: {ex.Message}"
                };
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        public string GetName()
        {
            return "Export Room Data";
        }
    }
}
