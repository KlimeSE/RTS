using System;
using System.Collections.Generic;
using System.Linq;
using AiEnabled.API;
using ProtoBuf;
using RichHudFramework.Client;
using RichHudFramework.UI;
using RichHudFramework.UI.Client;
using RichHudFramework.UI.Rendering;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using SpaceEngineers.Game.Entities.Blocks;
using SpaceEngineers.Game.EntityComponents.Blocks;
using SpaceEngineers.Game.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Input;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;


namespace klime.RTS
{
    public enum ViewState
    {
        Idle,
        GoToView,
        InView,
        GoToIdle,
        Test
    }

    public enum BuildState
    {
        Idle,
        GoToPicker,
        InPicker,
        GoToBuild,
        InBuild,
        GoToCreate,
        LoopCreate,
        GoToIdle
    }

    public enum BoxState
    {
        Idle,
        GoToBox,
        InBox,
        Decision,
        RunSingle,
        RunMulti,
        PostRun,
        GoToIdle,
    }

    public enum MoveState
    {
        Idle,
        GoToMove,
        InMove,
        GoToIdle
    }

    public class TargetRender
    {
        public MyStringId lowerMaterial;
        public MyStringId upperMaterial;
        public MyStringId lineMaterial;
        public Color lowerColor = Color.Red;
        public Color upperColor = Color.White;
        public Color lineColor = Color.Orange;

        public Vector3D start;
        public Vector3D end;
        public Vector3D left;
        public Vector3D up;

        public Vector3D origin;
        public double height;
        public double width = 0.2f;
        public double radius = 0.5f;
        public Vector2 uvOffset = Vector2.Zero;

        public Vector3D oriStart;
        public Vector2 initScreenPos;

        public IMyCubeGrid lockTargetGrid;

        public TargetRender(Vector3D start, Vector3D end, Vector3D left, Vector3D up, Vector3D oriStart, Vector2 initScreenPos, Vector3D cameraPos,
            IMyCubeGrid lockTargetGrid = null)
        {
            this.start = start;
            this.end = end;
            this.left = left;
            this.up = up;
            this.oriStart = oriStart;
            this.initScreenPos = initScreenPos;

            this.lowerMaterial = MyStringId.GetOrCompute("PointCircle");
            this.upperMaterial = MyStringId.GetOrCompute("PointCircle");
            this.lineMaterial = MyStringId.GetOrCompute("Square");

            this.lockTargetGrid = lockTargetGrid;

            ComputeDimensions(cameraPos);
        }

        public void ComputeDimensions(Vector3D cameraPos)
        {
            Vector3D startEnd = end - start;
            origin = start + (startEnd / 2);
            height = startEnd.Length() / 2;

            //Scaling with camera
            var distToCam = Vector3D.Distance(cameraPos, origin);
            width = MathHelper.Lerp(0.1, 0.3, distToCam / 100);
            radius = MathHelper.Lerp(0.5, 2, distToCam / 100);
        }

        public void UpdateRender(Vector3D cameraPos)
        {
            ComputeDimensions(cameraPos);
            ////Lower
            //MyTransparentGeometry.AddPointBillboard(lowerMaterial, lowerColor, start, 1f, 0f, -1, VRageRender.MyBillboard.BlendTypeEnum.PostPP);

            //Upper
            MyTransparentGeometry.AddPointBillboard(upperMaterial, upperColor, end, (float)radius, 0f, -1, VRageRender.MyBillboard.BlendTypeEnum.PostPP);

            //Line
            MyTransparentGeometry.AddBillboardOriented(lineMaterial, lineColor, origin, left, up, (float)width, (float)height, uvOffset,
                VRageRender.MyBillboard.BlendTypeEnum.PostPP);
        }
    }

    public class LineRender
    {
        public MyStringId material;
        public Vector3D start;
        public Vector3D end;
        public Vector3D left;
        public Vector3D up;
        public Color color = Color.DarkOliveGreen;

        public Vector3D origin;
        public double height;
        public double width = 0.2f;
        public Vector2 uvOffset = Vector2.Zero;

        public LineRender(Vector3D start, Vector3D end, Vector3D left, Vector3D up, Vector3D cameraPos)
        {
            this.start = start;
            this.end = end;
            this.left = left;
            this.up = up;

            this.material = MyStringId.GetOrCompute("Square");

            ComputeDimensions(cameraPos);
        }

        public void ComputeDimensions(Vector3D cameraPos)
        {
            Vector3D startEnd = end - start;
            origin = start + (startEnd / 2);
            height = startEnd.Length() / 2;

            //Scaling with camera
            var distToCam = Vector3D.Distance(cameraPos, origin);
            width = MathHelper.Lerp(0.1, 0.3, distToCam / 100);
        }

        public void UpdateRender(Vector3D cameraPos)
        {
            ComputeDimensions(cameraPos);
            MyTransparentGeometry.AddBillboardOriented(material, color, origin, left, up, (float)width, (float)height, uvOffset,
                VRageRender.MyBillboard.BlendTypeEnum.PostPP);
        }
    }

    public abstract class RTSUnit : LabelBox
    {
        private bool _inBox;
        private bool _isSelected;

        public bool inBox
        {
            get { return _inBox; }
            set
            {
                if (_inBox != value)
                {
                    _inBox = value;
                    UpdateColor();
                }
            }
        }

        public bool isSelected
        {
            get { return _isSelected; }
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    UpdateColor();
                }
            }
        }

        public abstract void UpdateColor();

        public abstract void MoveTo(Vector3D target, Vector3D adjustedTarget);

        public abstract void Close();
    }

    public class RTSCharacter: RTSUnit
    {
        public IMyCharacter character;
        public Material squareMat;
        public Color defaultColor = Color.White;
        public Color inBoxColor = Color.Orange;
        public Color selectedColor = Color.Green;

        //Move vars
        public MoveState currentMoveState = MoveState.Idle;
        public Vector3D currentTarget;
        public Vector3D currentAdjustedTarget;
        public List<LineRender> lineRenders = new List<LineRender>();

        public RTSCharacter(IMyCharacter character, bool inBox = false, bool isSelected = false)
        {
            //Icon
            squareMat = new Material(MyStringId.GetOrCompute("Square"), Vector2.Zero);
            this.character = character;

            this.AutoResize = false;
            this.background.Material = squareMat;
            this.Size = new Vector2(8, 8);
            this.Text = "";

            if (inBox) this.Color = inBoxColor;
            else if (isSelected) this.Color = selectedColor;
            else this.Color = defaultColor;
        }

        public override void UpdateColor()
        {
            if (isSelected) this.Color = selectedColor;
            else if (inBox) this.Color = inBoxColor;
            else this.Color = defaultColor;
        }

        protected override void Draw()
        {
            if (character == null || character.IsDead || character.MarkedForClose)
            {
                Close();
                return;
            }

            var camPos = MyAPIGateway.Session.Camera.WorldMatrix.Translation;

            var worldPosRaw = character.WorldAABB.Center;
            var screenPosRaw = MyAPIGateway.Session.Camera.WorldToScreen(ref worldPosRaw);
            var screenPos = new Vector2D(MathHelper.Clamp(screenPosRaw.X, -1, 1), MathHelper.Clamp(screenPosRaw.Y, -1, 1));

            double resX = 1920;
            double resY = 1080;

            var sResX = MathHelper.Lerp(-(resX / 2), resX / 2, (screenPos.X + 1) / 2);
            var sResY = MathHelper.Lerp(-(resY / 2), resY / 2, (screenPos.Y + 1) / 2);
            this.Offset = new Vector2((float)sResX, (float)sResY);

            if (currentMoveState == MoveState.InMove && isSelected)
            {
                lineRenders.Clear();
                Vector3D start = character.WorldAABB.Center;
                Vector3D end = currentTarget;
                Vector3D startEnd = end - start;
                Vector3D upVec = Vector3D.Normalize(startEnd);
                Vector3D leftVec = Vector3D.Normalize(Vector3D.CalculatePerpendicularVector(Vector3D.Up));

                LineRender singleLine = new LineRender(start, end, leftVec, upVec, camPos);
                lineRenders.Add(singleLine);

                foreach (var line in lineRenders)
                {
                    line.UpdateRender(camPos);
                }
            }
        }

        public override void MoveTo(Vector3D target, Vector3D adjustedTarget)
        {
            if (character == null || character.IsDead) return;

            currentTarget = target;
            currentAdjustedTarget = adjustedTarget;

            CharacterMovePacket movePacket = new CharacterMovePacket(character.EntityId, currentAdjustedTarget);
            var byteArray = MyAPIGateway.Utilities.SerializeToBinary(movePacket);
            MyAPIGateway.Multiplayer.SendMessageTo(RTS.rtsInstance.netId, byteArray, MyAPIGateway.Multiplayer.ServerId);

            currentMoveState = MoveState.InMove;
        }

        public override void Close()
        {
            this.Unregister();
        }
    }

    public class RTSGrid : RTSUnit
    {
        public MyCubeGrid grid;
        public IMyFlightMovementBlock flightBlock;
        public Material squareMat;
        public Color defaultColor = Color.White;
        public Color inBoxColor = Color.Orange;
        public Color selectedColor = Color.Green;

        //Move vars
        public MoveState currentMoveState = MoveState.Idle;
        public Vector3D currentTarget;
        public Vector3D currentAdjustedTarget;
        public List<LineRender> lineRenders = new List<LineRender>();

        public RTSGrid(MyCubeGrid grid, bool inBox = false, bool isSelected = false)
        {
            //Icon
            squareMat = new Material(MyStringId.GetOrCompute("Square"), Vector2.Zero);
            this.grid = grid;

            this.AutoResize = false;
            this.background.Material = squareMat;
            this.Size = new Vector2(10, 10);
            this.Text = "";

            if (inBox) this.Color = inBoxColor;
            else if (isSelected) this.Color = selectedColor;
            else this.Color = defaultColor;

            CreateGridUnit();
        }

        private void CreateGridUnit()
        {
            foreach (var block in grid.GetFatBlocks())
            {
                IMyFlightMovementBlock testBlock = block as IMyFlightMovementBlock;
                if (testBlock != null)
                {
                    flightBlock = testBlock;
                    break;
                }
            }

            if (flightBlock != null)
            {
                var autopilotComp = flightBlock.Components.Get<MyAutopilotComponent>();
                if (autopilotComp != null)
                {
                    autopilotComp.OnWaypointReached += WaypointComplete;
                }
            }
        }

        private void WaypointComplete(MyAutopilotWaypoint obj)
        {
            if (currentMoveState == MoveState.InMove)
            {
                currentMoveState = MoveState.Idle;
            }
        }

        public override void UpdateColor()
        {
            if (isSelected) this.Color = selectedColor;
            else if (inBox) this.Color = inBoxColor;
            else this.Color = defaultColor;
        }

        protected override void Draw()
        {
            var camPos = MyAPIGateway.Session.Camera.WorldMatrix.Translation;
            if (flightBlock == null || grid == null || grid.MarkedForClose || flightBlock.MarkedForClose || !flightBlock.IsWorking)
            {
                Close();
                return;
            }

            var worldPosRaw = grid.PositionComp.WorldAABB.Center;
            var screenPosRaw = MyAPIGateway.Session.Camera.WorldToScreen(ref worldPosRaw);
            var screenPos = new Vector2D(MathHelper.Clamp(screenPosRaw.X, -1, 1), MathHelper.Clamp(screenPosRaw.Y, -1, 1));

            double resX = 1920;
            double resY = 1080;

            var sResX = MathHelper.Lerp(-(resX / 2), resX / 2, (screenPos.X + 1) / 2);
            var sResY = MathHelper.Lerp(-(resY / 2), resY / 2, (screenPos.Y + 1) / 2);
            this.Offset = new Vector2((float)sResX, (float)sResY);

            if (currentMoveState == MoveState.InMove && isSelected)
            {
                lineRenders.Clear();
                Vector3D start = grid.PositionComp.WorldAABB.Center;
                Vector3D end = currentTarget;
                Vector3D startEnd = end - start;
                Vector3D upVec = Vector3D.Normalize(startEnd);
                Vector3D leftVec = Vector3D.Normalize(Vector3D.CalculatePerpendicularVector(Vector3D.Up));

                LineRender singleLine = new LineRender(start, end, leftVec, upVec, camPos);
                lineRenders.Add(singleLine);

                foreach (var line in lineRenders)
                {
                    line.UpdateRender(camPos);
                }
            }
        }

        public override void MoveTo(Vector3D target, Vector3D adjustedTarget)
        {
            if (flightBlock == null || grid == null || !flightBlock.IsWorking) return;

            currentTarget = target;
            currentAdjustedTarget = adjustedTarget;

            GridMovePacket movePacket = new GridMovePacket(flightBlock.EntityId, currentAdjustedTarget);
            var byteArray = MyAPIGateway.Utilities.SerializeToBinary(movePacket);
            MyAPIGateway.Multiplayer.SendMessageTo(RTS.rtsInstance.netId, byteArray, MyAPIGateway.Multiplayer.ServerId);

            currentMoveState = MoveState.InMove;
        }

        public void Attack(IMyCubeGrid targetGrid)
        {
            if (flightBlock == null || grid == null || !flightBlock.IsWorking) return;

            currentTarget = targetGrid.WorldAABB.Center;
            currentAdjustedTarget = currentTarget;

            var distToTarget = Vector3D.Distance(grid.PositionComp.WorldAABB.Center, currentTarget);

            if (distToTarget > 100)
            {
                var dir = Vector3D.Normalize(currentTarget - grid.PositionComp.WorldAABB.Center);
                currentAdjustedTarget = grid.PositionComp.WorldAABB.Center + (dir * (distToTarget - 100));
                GridMovePacket movePacket = new GridMovePacket(flightBlock.EntityId, currentAdjustedTarget);
                var moveArray = MyAPIGateway.Utilities.SerializeToBinary(movePacket);
                MyAPIGateway.Multiplayer.SendMessageTo(RTS.rtsInstance.netId, moveArray, MyAPIGateway.Multiplayer.ServerId);
            }

            GridAttackPacket attackPacket = new GridAttackPacket(targetGrid, grid);
            var attackArray = MyAPIGateway.Utilities.SerializeToBinary(attackPacket);
            MyAPIGateway.Multiplayer.SendMessageTo(RTS.rtsInstance.netId, attackArray, MyAPIGateway.Multiplayer.ServerId);
        }

        public override void Close()
        {
            if (flightBlock != null)
            {
                var autopilotComp = flightBlock.Components.Get<MyAutopilotComponent>();
                if (autopilotComp != null)
                {
                    autopilotComp.OnWaypointReached -= WaypointComplete;
                }
            }

            this.Unregister();
        }
    }

    public class GridBin
    {
        public double boundingVolume;
        public List<RTSGrid> grids = new List<RTSGrid>();

        public GridBin(double boundingVolume, List<RTSGrid> grids)
        {
            this.boundingVolume = boundingVolume;
            this.grids = new List<RTSGrid>(grids);
        }
    }

    public class CustomMouse : LabelBox
    {
        public BoxState currentBoxState = BoxState.Idle;
        public TexturedBox tBox;
        public TargetRender currentTargetRender;
        public Vector2 tBoxClickPos;
        List<IHitInfo> hits = new List<IHitInfo>();

        public CustomMouse()
        {
            this.Register(HudMain.HighDpiRoot, true);
            this.background.Material = new Material(MyStringId.GetOrCompute("MouseCursor"), Vector2.Zero);
            this.AutoResize = false;
            this.background.Color = new Color(255, 255, 255, 255);
            this.Size = new Vector2(50, 50);
            this.Text = "";
            this.ZOffset = 127;
        }

        protected override void HandleInput(Vector2 cursorPos)
        {
            base.HandleInput(cursorPos);

            var viewportSize = MyAPIGateway.Session.Camera.ViewportSize;
            var renderResFX = viewportSize.X / 1920;
            var renderResFY = viewportSize.Y / 1080;

            //Cursor
            //var mPos = MyAPIGateway.Input.GetMousePosition();
            //var oPosX = (mPos.X / renderResFX) - (1920 / 2);
            //var oPosY = -1 * ((mPos.Y / renderResFY) - (1080 / 2));
            //this.Offset = new Vector2(oPosX, oPosY);


            var mouseDelta = MyAPIGateway.Input.GetCursorPositionDelta();
            this.Offset += new Vector2(mouseDelta.X, -mouseDelta.Y);
            this.Offset = new Vector2(MathHelper.Clamp(this.Offset.X, -960, 960), MathHelper.Clamp(this.Offset.Y, -540, 540));
            var camPos = MyAPIGateway.Session.Camera.WorldMatrix.Translation;


            if (MyAPIGateway.Input.IsNewLeftMousePressed() && !MyAPIGateway.Input.IsKeyPress(MyKeys.R))
            {
                if (currentBoxState == BoxState.Idle)
                {
                    currentBoxState = BoxState.GoToBox;
                }
            }

            if (MyAPIGateway.Input.IsNewLeftMouseReleased())
            {
                if (currentBoxState != BoxState.Idle)
                {
                    currentBoxState = BoxState.Decision;
                }

                if (RTS.rtsInstance.isRotating)
                {
                    RTS.rtsInstance.isRotating = false;
                }
            }
            
            if (MyAPIGateway.Input.IsNewRightMousePressed())
            {
                Vector3D worldPos = new Vector3D(this.Position.X, this.Position.Y, -0.00000001);
                worldPos = Vector3D.Transform(worldPos, this.HudSpace.PlaneToWorld);
                var dirFromCam = Vector3D.Normalize(worldPos - camPos);

                hits.Clear();
                MyAPIGateway.Physics.CastRay(camPos, camPos + (dirFromCam * 1000), hits);

                if (hits.Count > 0)
                {
                    hits.OrderBy(x => Vector3D.Distance(worldPos, x.Position));
                    IHitInfo selectedHit = null;
                    bool hitGrid = false;

                    foreach (var hit in hits)
                    {
                        if (hit.HitEntity != null)
                        {
                            IMyCubeGrid grid = hit.HitEntity as IMyCubeGrid;
                            if (grid != null && grid.Physics != null)
                            {
                                var rel = MyIDModule.GetRelationPlayerBlock(grid.BigOwners.FirstOrDefault(), MyAPIGateway.Session.Player.IdentityId);

                                if (rel == MyRelationsBetweenPlayerAndBlock.Enemies)
                                {
                                    hitGrid = true;
                                    selectedHit = hit;
                                    break;
                                }
                            }
                        }
                    }


                    if (selectedHit == null)
                    {
                        foreach (var hit in hits)
                        {
                            if (hit.HitEntity != null)
                            {
                                MyVoxelBase voxel = hit.HitEntity as MyVoxelBase;
                                if (voxel != null)
                                {
                                    selectedHit = hit;
                                    break;
                                }
                            }
                        }
                    }


                    if (selectedHit != null)
                    {

                        if (hitGrid)
                        {
                            IMyCubeGrid grid = selectedHit.HitEntity as IMyCubeGrid;
                            Vector3D centralPos = Vector3D.Zero;
                            for (int i = 0; i < RTS.rtsInstance.selectedGridIndicies.Count; i++)
                            {
                                var avIndex = RTS.rtsInstance.selectedGridIndicies[i];
                                centralPos += RTS.rtsInstance.availableGrids[avIndex].grid.WorldMatrix.Translation;
                            }

                            for (int i = 0; i < RTS.rtsInstance.selectedCharIndicies.Count; i++)
                            {
                                var avIndex = RTS.rtsInstance.selectedCharIndicies[i];
                                centralPos += RTS.rtsInstance.avaiableCharacters[avIndex].character.WorldAABB.Center;
                            }

                            centralPos *= (1d / (RTS.rtsInstance.selectedGridIndicies.Count + RTS.rtsInstance.selectedCharIndicies.Count));
                            var centralAlt = Vector3D.Distance(centralPos, RTS.rtsInstance.nearPlanet.GetClosestSurfacePointGlobal(centralPos));

                            var targetCentralUpVec = Vector3D.Normalize(selectedHit.Position - RTS.rtsInstance.nearPlanet.PositionComp.GetPosition());
                            var targetCentralLeftVec = Vector3D.Normalize(Vector3D.CalculatePerpendicularVector(targetCentralUpVec));
                            var targetCentralPos = grid.WorldAABB.Center;

                            currentTargetRender = new TargetRender(selectedHit.Position, targetCentralPos, targetCentralLeftVec, targetCentralUpVec,
                                centralPos, this.Position, camPos ,grid);
                        }
                        else //Voxel
                        {
                            Vector3D centralPos = Vector3D.Zero;
                            for (int i = 0; i < RTS.rtsInstance.selectedGridIndicies.Count; i++)
                            {
                                var avIndex = RTS.rtsInstance.selectedGridIndicies[i];
                                centralPos += RTS.rtsInstance.availableGrids[avIndex].grid.WorldMatrix.Translation;
                            }

                            for (int i = 0; i < RTS.rtsInstance.selectedCharIndicies.Count; i++)
                            {
                                var avIndex = RTS.rtsInstance.selectedCharIndicies[i];
                                centralPos += RTS.rtsInstance.avaiableCharacters[avIndex].character.WorldAABB.Center;
                            }

                            centralPos *= (1d / (RTS.rtsInstance.selectedGridIndicies.Count + RTS.rtsInstance.selectedCharIndicies.Count));
                            var centralAlt = Vector3D.Distance(centralPos, RTS.rtsInstance.nearPlanet.GetClosestSurfacePointGlobal(centralPos));

                            var targetCentralUpVec = Vector3D.Normalize(selectedHit.Position - RTS.rtsInstance.nearPlanet.PositionComp.GetPosition());
                            var targetCentralLeftVec = Vector3D.Normalize(Vector3D.CalculatePerpendicularVector(targetCentralUpVec));
                            var targetCentralPos = selectedHit.Position + (targetCentralUpVec * centralAlt);

                            currentTargetRender = new TargetRender(selectedHit.Position, targetCentralPos, targetCentralLeftVec, targetCentralUpVec,
                                centralPos, this.Position, camPos);
                        }
                    }

                }
            }

            if (MyAPIGateway.Input.IsNewRightMouseReleased())
            {
                if (currentTargetRender != null)
                {
                    if (currentTargetRender.lockTargetGrid != null)
                    {
                        for (int i = 0; i < RTS.rtsInstance.selectedGridIndicies.Count; i++)
                        {
                            var avIndex = RTS.rtsInstance.selectedGridIndicies[i];

                            RTS.rtsInstance.availableGrids[avIndex].Attack(currentTargetRender.lockTargetGrid);
                        }

                        for (int i = 0; i < RTS.rtsInstance.selectedCharIndicies.Count; i++)
                        {
                            var avIndex = RTS.rtsInstance.selectedCharIndicies[i];
                            var adjustedTargetPos = currentTargetRender.end;

                            RTS.rtsInstance.avaiableCharacters[avIndex].MoveTo(currentTargetRender.end, adjustedTargetPos);
                        }
                    }
                    else //Voxel
                    {
                        for (int i = 0; i < RTS.rtsInstance.selectedGridIndicies.Count; i++)
                        {
                            var avIndex = RTS.rtsInstance.selectedGridIndicies[i];
                            var adjustedTargetPos = currentTargetRender.end + (RTS.rtsInstance.availableGrids[avIndex].grid.WorldMatrix.Translation - currentTargetRender.oriStart);

                            RTS.rtsInstance.availableGrids[avIndex].MoveTo(currentTargetRender.end, adjustedTargetPos);
                        }

                        for (int i = 0; i < RTS.rtsInstance.selectedCharIndicies.Count; i++)
                        {
                            var avIndex = RTS.rtsInstance.selectedCharIndicies[i];
                            var adjustedTargetPos = currentTargetRender.end;

                            RTS.rtsInstance.avaiableCharacters[avIndex].MoveTo(currentTargetRender.end, adjustedTargetPos);
                        }
                    }
                }
            }

            if (MyAPIGateway.Input.IsRightMousePressed())
            {
                if (currentTargetRender != null && currentTargetRender.lockTargetGrid == null)
                {
                    var oldMousePos = currentTargetRender.initScreenPos;
                    var currentMousePos = this.Position;
                    var yDiff = 0.1 * (currentMousePos.Y - oldMousePos.Y);

                    var currentAlt = (currentTargetRender.end - currentTargetRender.start).Length();
                    var currentUpVec = Vector3D.Normalize(currentTargetRender.start - RTS.rtsInstance.nearPlanet.PositionComp.GetPosition());
                    var finalLength = MathHelperD.Max(currentAlt + yDiff, 0.5);
                    var finalPos = currentTargetRender.start + (currentUpVec * (finalLength));

                    currentTargetRender.end = finalPos;
                    currentTargetRender.ComputeDimensions(camPos);

                    this.Offset = new Vector2(currentTargetRender.initScreenPos.X, this.Offset.Y);
                    currentTargetRender.initScreenPos = this.Position;
                }
            }

            if (MyAPIGateway.Input.IsNewKeyPressed(MyKeys.F))
            {
                //Dictionary<double, List<RTSGrid>> volumeGrids = new Dictionary<double, List<RTSGrid>>();
                //List<GridBin> binnedGrids = new List<GridBin>();

                //var formationAveragePos = Vector3D.Zero;
                //for (int i = 0; i < RTS.rtsInstance.selectedGridIndicies.Count; i++)
                //{
                //    var avIndex = RTS.rtsInstance.selectedGridIndicies[i];
                //    var rtsGrid = RTS.rtsInstance.availableGrids[avIndex];

                //    var volume = Math.Round(rtsGrid.grid.PositionComp.WorldVolume.Radius, 2);

                //    if (!volumeGrids.ContainsKey(volume))
                //    {
                //        volumeGrids.Add(volume, new List<RTSGrid>());
                //    }
                //    volumeGrids[volume].Add(rtsGrid);

                //    formationAveragePos += rtsGrid.grid.PositionComp.WorldAABB.Center;
                //}

                //formationAveragePos *= (1d / RTS.rtsInstance.selectedGridIndicies.Count);

                //var formationWidth = Math.Min()

                //var formationUpVec = Vector3D.Normalize(formationStartPos - RTS.rtsInstance.nearPlanet.PositionComp.GetPosition());
                //var formationPlane = new PlaneD(formationStartPos, formationUpVec);
                //var camRight = formationStartPos + MyAPIGateway.Session.Camera.WorldMatrix.Right;
                //var formationRightVec = Vector3D.Normalize(formationPlane.ProjectPoint(ref camRight) - formationStartPos);

                //MatrixD formationMatrix = MatrixD.CreateWorld(formationStartPos, formationRightVec, formationUpVec);

                //int forCount = 0;
                //int rightCount = 0;
                //int maxForCount = 5;

                //foreach (var volume in volumeGrids.Keys.ToList())
                //{
                //    binnedGrids.Add(new GridBin(volume, volumeGrids[volume]));
                //}
                //binnedGrids.Sort((x, y) => x.boundingVolume.CompareTo(y.boundingVolume));

                //foreach (var bin in binnedGrids)
                //{
                //    forCount = 0;
                //    rightCount = 0;

                //    List<Vector3D> targets = new List<Vector3D>();
                //    for (int i = bin.grids.Count - 1; i >= 0; i--)
                //    {
                //        if (forCount % maxForCount == 0)
                //        {
                //            forCount = 0;
                //            rightCount++;
                //        }

                //        Vector3D targetPos = formationMatrix.Translation + (formationMatrix.Forward * forCount * (bin.boundingVolume * 4))
                //            + (formationMatrix.Right * rightCount * (bin.boundingVolume * 4));
                //        targets.Add(targetPos);
                //        forCount++;
                //    }

                //    foreach (var target in targets)
                //    {
                //        RTSGrid pickGrid = null;
                //        double minDist = double.MaxValue;

                //        for (int i = bin.grids.Count - 1; i >= 0; i--)
                //        {
                //            var grid = bin.grids[i];
                //            var dist = (grid.grid.PositionComp.WorldAABB.Center - target).LengthSquared();
                //            if (dist < minDist)
                //            {
                //                minDist = dist;
                //                pickGrid = grid;
                //            }
                //        }

                //        if (pickGrid != null)
                //        {
                //            pickGrid.MoveTo(target, target);
                //            bin.grids.Remove(pickGrid);
                //        }
                //    }
                //}
            }
        }

        protected override void Draw()
        {
            base.Draw();

            if (currentBoxState == BoxState.Idle)
            {

            }

            if (currentBoxState == BoxState.GoToBox)
            {
                //Selected grids
                foreach (var avGrid in RTS.rtsInstance.availableGrids)
                {
                    avGrid.isSelected = false;
                }

                //Selected chars
                foreach (var avChar in RTS.rtsInstance.avaiableCharacters)
                {
                    avChar.isSelected = false;
                }

                if (tBox == null)
                {
                    tBox = new TexturedBox();
                    tBox.Register(HudMain.HighDpiRoot, true);
                }

                tBox.Visible = true;
                tBox.Material = new Material(MyStringId.GetOrCompute("Square"), Vector2.Zero);
                tBox.Size = new Vector2(0, 0);
                tBox.Color = new Color(Color.Black, 0.7f);
                tBoxClickPos = this.Offset;
                currentTargetRender = null;
                currentBoxState = BoxState.InBox;
            }

            if (currentBoxState == BoxState.InBox)
            {
                var actualWidth = this.Offset.X - tBoxClickPos.X;
                var actualHeight = this.Offset.Y - tBoxClickPos.Y;

                tBox.Offset = new Vector2(tBoxClickPos.X + (actualWidth / 2), tBoxClickPos.Y + (actualHeight / 2));
                tBox.Size = new Vector2(actualWidth, actualHeight);

                var tboxXMin = Math.Min(tBoxClickPos.X, this.Offset.X);
                var tboxXMax = Math.Max(tBoxClickPos.X, this.Offset.X);

                var tboxYMin = Math.Min(tBoxClickPos.Y, this.Offset.Y);
                var tboxYMax = Math.Max(tBoxClickPos.Y, this.Offset.Y);

                foreach (var avGrid in RTS.rtsInstance.availableGrids)
                {
                    bool inBox = false;
                    if ((avGrid.Position.X >= tboxXMin && avGrid.Position.X <= tboxXMax)
                        && (avGrid.Position.Y >= tboxYMin && avGrid.Position.Y <= tboxYMax))
                    {
                        inBox = true;
                    }

                    avGrid.inBox = inBox;
                }

                foreach (var avChar in RTS.rtsInstance.avaiableCharacters)
                {
                    bool inBox = false;
                    if ((avChar.Position.X >= tboxXMin && avChar.Position.X <= tboxXMax)
                        && (avChar.Position.Y >= tboxYMin && avChar.Position.Y <= tboxYMax))
                    {
                        inBox = true;
                    }

                    avChar.inBox = inBox;
                }
            }

            if (currentBoxState == BoxState.Decision)
            {
                var boxSize = tBox.Size.Length();
                if (boxSize < 8)
                {
                    currentBoxState = BoxState.RunSingle;
                }
                else
                {
                    currentBoxState = BoxState.RunMulti;
                }
            }

            if (currentBoxState == BoxState.RunSingle)
            {
                RTSGrid closestAvGrid = null;
                double currentClosestGridDist = 1000;

                RTSCharacter closestAvChar = null;
                double currentClosestCharDist = 1000;

                foreach (var avGrid in RTS.rtsInstance.availableGrids)
                {
                    var dist = Vector2.Distance(avGrid.Position, this.Position);
                    if (dist < 25)
                    {
                        if (dist < currentClosestGridDist)
                        {
                            currentClosestGridDist = dist;
                            closestAvGrid = avGrid;
                        }
                    }
                }

                foreach (var avChar in RTS.rtsInstance.avaiableCharacters)
                {
                    var dist = Vector2.Distance(avChar.Position, this.Position);
                    if (dist < 25)
                    {
                        if (dist < currentClosestCharDist)
                        {
                            currentClosestCharDist = dist;
                            closestAvChar = avChar;
                        }
                    }
                }

                if (closestAvChar != null)
                {
                    closestAvChar.isSelected = true;
                }

                if (closestAvGrid != null)
                {
                    closestAvGrid.isSelected = true;
                    if (closestAvChar != null)
                    {
                        closestAvChar.isSelected = false;
                    }
                }

                currentBoxState = BoxState.PostRun;
            }

            if (currentBoxState == BoxState.RunMulti)
            {
                foreach (var avGrid in RTS.rtsInstance.availableGrids)
                {
                    if (avGrid.inBox)
                    {
                        avGrid.isSelected = true;
                    }
                }

                foreach (var avChar in RTS.rtsInstance.avaiableCharacters)
                {
                    if (avChar.inBox)
                    {
                        avChar.isSelected = true;
                    }
                }

                currentBoxState = BoxState.PostRun;
            }

            if (currentBoxState == BoxState.PostRun)
            {
                RTS.rtsInstance.selectedGridIndicies.Clear();
                RTS.rtsInstance.selectedCharIndicies.Clear();

                for (int i = 0; i < RTS.rtsInstance.availableGrids.Count; i++)
                {
                    if (RTS.rtsInstance.availableGrids[i].isSelected)
                    {
                        RTS.rtsInstance.selectedGridIndicies.Add(i);
                    }
                }

                for (int i = 0; i < RTS.rtsInstance.avaiableCharacters.Count; i++)
                {
                    if (RTS.rtsInstance.avaiableCharacters[i].isSelected)
                    {
                        RTS.rtsInstance.selectedCharIndicies.Add(i);
                    }
                }

                currentBoxState = BoxState.GoToIdle;
            }

            if (currentBoxState == BoxState.GoToIdle)
            {
                tBox.Visible = false;
                tBoxClickPos = Vector2.Zero;
                currentBoxState = BoxState.Idle;
            }

            var camPos = MyAPIGateway.Session.Camera.WorldMatrix.Translation;
            currentTargetRender?.UpdateRender(camPos);
        }

        public void Close()
        {
            this.Unregister();

            tBox?.Unregister();
            tBox = null;

            currentTargetRender = null;
        }
    }

    public class BuildGrid : LabelBoxButton
    {
        public MyObjectBuilder_CubeGrid buildGridOB;
        public MyStringId buildTextureID;
        public string buildGridName;
        public Color defaultColor = Vector4.One;
        public Color highlightColor = Color.SkyBlue;

        public BuildGrid(HudParentBase parent, string buildGridName, Vector2 offset)
        {
            this.buildGridName = buildGridName;
            this.buildTextureID = MyStringId.GetOrCompute(buildGridName);

            this.Register(parent, true);
            this.AutoResize = false;
            this.background.Material = new Material(buildTextureID, Vector2.Zero);
            this.Color = defaultColor;
            this.Text = "";
            this.Size = new Vector2(240, 120);
            this.Offset = offset;
        }

        public void Update(Vector2 mousePos)
        {
            var xMin = this.Position.X - (this.Size.X / 2);
            var xMax = this.Position.X + (this.Size.X / 2);
            var yMin = this.Position.Y - (this.Size.Y / 2);
            var yMax = this.Position.Y + (this.Size.Y / 2);
            if (mousePos.X > xMin && mousePos.X < xMax)
            {
                if (mousePos.Y > yMin && mousePos.Y < yMax)
                {
                    this.Color = highlightColor;
                    if (MyAPIGateway.Input.IsNewLeftMousePressed())
                    {
                        DoClick();
                    }
                }
                else
                {
                    this.Color = defaultColor;
                }
            }
            else
            {
                this.Color = defaultColor;
            }
        }

        public void DoClick()
        {
            RTS.rtsInstance.BuildRequest(buildGridName);
        }

        public void Close()
        {
            this.Unregister();
        }
    }

    public class BuildUI : LabelBox
    {
        public MyStringId buildUITexture;
        public List<BuildGrid> buildGrids = new List<BuildGrid>();
        public List<string> buildPrefabNames = new List<string>();

        public Color defaultColor = new Color(41, 54, 62, 200);
        public BuildUI(List<string> buildPrefabNames)
        {
            this.buildPrefabNames = new List<string>(buildPrefabNames);
            this.Register(HudMain.HighDpiRoot, true);


            this.background.Material = new Material(MyStringId.GetOrCompute("RTSSquare"), Vector2.Zero);
            this.AutoResize = false;
            this.background.Color = defaultColor;
            this.Size = new Vector2(700, 150);
            this.Offset = new Vector2(-500, -450);
            this.Text = "";

            Vector2 gridOffset = new Vector2(-240, 0);
            foreach (var name in this.buildPrefabNames)
            {
                BuildGrid bGrid = new BuildGrid(this, name, gridOffset);
                buildGrids.Add(bGrid);
                gridOffset += new Vector2(240, 0);
            }

        }

        public void Update(Vector2 mousePos)
        {
            foreach (var bGrid in this.buildGrids)
            {
                bGrid.Update(mousePos);
            }
        }

        public void Close()
        {
            foreach (var bGrid in this.buildGrids)
            {
                bGrid.Close();
            }

            this.Unregister();
        }
    }

    [ProtoInclude(3000, typeof(GridAttackPacket))]
    [ProtoInclude(2000, typeof(CharacterMovePacket))]
    [ProtoInclude(1000, typeof(GridMovePacket))]
    [ProtoContract]
    public abstract class Packet
    {
        public Packet()
        {

        }
    }

    [ProtoContract]
    public class CharacterMovePacket : Packet
    {
        [ProtoMember(10)]
        public Vector3D adjustedPos;

        [ProtoMember(11)]
        public long characterEntID;

        public CharacterMovePacket()
        {

        }

        public CharacterMovePacket(long characterEntID, Vector3D adjustedPos)
        {
            this.characterEntID = characterEntID;
            this.adjustedPos = adjustedPos;
        }
    }

    [ProtoContract]
    public class GridMovePacket : Packet
    {
        [ProtoMember(1)]
        public Vector3D adjustedPos;

        [ProtoMember(2)]
        public long flightBlockID;

        public GridMovePacket()
        {

        }

        public GridMovePacket(long flightBlockID, Vector3D adjustedPos)
        {
            this.flightBlockID = flightBlockID;
            this.adjustedPos = adjustedPos;
        }
    }

    [ProtoContract]
    public class GridAttackPacket : Packet
    {

        [ProtoMember(20)]
        public long targetGridID;

        [ProtoMember(21)]
        public long myGridID;

        public GridAttackPacket()
        {

        }

        public GridAttackPacket(IMyCubeGrid targetGrid, IMyCubeGrid myGrid)
        {
            this.targetGridID = targetGrid.EntityId;
            this.myGridID = myGrid.EntityId;
        }
    }

    public class DelayGrid
    {
        public IMyCubeGrid grid;
        public int runTime;

        public DelayGrid(IMyCubeGrid grid, int runTime)
        {
            this.grid = grid;
            this.runTime = runTime;
        }
    }

    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation | MyUpdateOrder.BeforeSimulation)]
    public class RTS : MySessionComponentBase
    {
        public ViewState currentViewState = ViewState.Idle;
        bool validInputThisTick = false;
        MySpectator spectator;
        public MyPlanet nearPlanet;
        int viewAnimFrame;
        int maxViewAnimFrame = 60;
        public MatrixD freezeMatrix = MatrixD.Identity;
        public MatrixD workingMatrix = MatrixD.Identity;
        public Vector3D workingFocus = Vector3D.Zero;
        public Vector3D workingCameraVelocity = Vector3D.Zero;
        public List<IHitInfo> workingHits = new List<IHitInfo>();

        double spectatorScaling = (5.0 / 3.0) * 0.35;

        //Rotation vars
        public Vector2 rotPrev;
        public Vector2 rotVel;
        public bool isRotating = false;

        double pHeightMin = 10;
        double pHeightMax = 200;
        double pHeightDefault = 25;

        public List<RTSGrid> availableGrids = new List<RTSGrid>();
        public List<RTSCharacter> avaiableCharacters = new List<RTSCharacter>();
        public List<int> selectedGridIndicies = new List<int>();
        public List<int> selectedCharIndicies = new List<int>();
        List<MyEntity> searchEnts = new List<MyEntity>();
        List<IMyTerminalBlock> searchBlocks = new List<IMyTerminalBlock>();

        //Networking
        public static RTS rtsInstance;
        public ushort netId = 28839;

        //AiEnabled API
        RemoteBotAPI aiEnabledAPI;
        CustomMouse mouse;

        //Building
        BuildState currentBuildState = BuildState.Idle;
        BuildUI currentBuildUI;
        List<string> buildPrefabNames = new List<string>
        {
            //"Barracks",
            //"TankFactory",
            //"AircraftFactory",
            "barrack",
            "factory",
            "airbase"
        };

        string currentBuildGridName = "";
        List<IMyCubeGrid> incomingPrefab = new List<IMyCubeGrid>();
        List<DelayGrid> delayGrids = new List<DelayGrid>();
        MyCubeGrid fakeBuildGrid;
        List<IHitInfo> hits = new List<IHitInfo>();
        MyObjectBuilder_CubeGrid currentOB;


        int masterTimer = 0;

        public float InQuint(float t) => t * t * t * t * t;
        public float OutQuint(float t) => 1 - InQuint(1 - t);

        public double InCubic(double t) => t * t * t;


        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            if (!MyAPIGateway.Utilities.IsDedicated)
            {
                RichHudClient.Init("RTS", InitComplete, ClientReset);
            }

            if (MyAPIGateway.Session.IsServer)
            {
                MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(netId, NetworkHandler);
                aiEnabledAPI = new RemoteBotAPI();
            }

            MyVisualScriptLogicProvider.PrefabSpawnedDetailed += PrefabSpawned;
            rtsInstance = this;
        }

        private void PrefabSpawned(long entityId, string prefabName)
        {
            if(buildPrefabNames.Contains(prefabName))
            {
                var grid = MyAPIGateway.Entities.GetEntityById(entityId) as IMyCubeGrid;
                DelayGrid dGrid = new DelayGrid(grid, masterTimer + 5);
                delayGrids.Add(dGrid);
            }
        }

        private void NetworkHandler(ushort arg1, byte[] arg2, ulong arg3, bool arg4)
        {
            Packet packet = MyAPIGateway.Utilities.SerializeFromBinary<Packet>(arg2);
            if (packet == null) return;

            GridMovePacket gridMovePacket = packet as GridMovePacket;
            if (gridMovePacket != null)
            {
                IMyFlightMovementBlock flightBlock = MyAPIGateway.Entities.GetEntityById(gridMovePacket.flightBlockID) as IMyFlightMovementBlock;
                if (flightBlock != null && flightBlock.IsWorking)
                {
                    MoveToGridPosition(flightBlock, gridMovePacket.adjustedPos);
                }
            }

            CharacterMovePacket characterMovePacket = packet as CharacterMovePacket;
            if (characterMovePacket != null)
            {
                IMyCharacter characterBot = MyAPIGateway.Entities.GetEntityById(characterMovePacket.characterEntID) as IMyCharacter;
                if (characterBot != null && !characterBot.IsDead)
                {
                    MoveToCharacterPosition(characterBot, characterMovePacket.adjustedPos);
                }
            }

            GridAttackPacket gridAttackPacket = packet as GridAttackPacket;
            if (gridAttackPacket != null)
            {
                IMyCubeGrid targetGrid = MyAPIGateway.Entities.GetEntityById(gridAttackPacket.targetGridID) as IMyCubeGrid;
                IMyCubeGrid myGrid = MyAPIGateway.Entities.GetEntityById(gridAttackPacket.myGridID) as IMyCubeGrid;
                if (targetGrid != null && targetGrid.Physics != null && !targetGrid.MarkedForClose
                    && myGrid != null && myGrid.Physics != null && !myGrid.MarkedForClose)
                {
                    AttackGrid(targetGrid, myGrid);
                }
            }
        }

        private void AttackGrid(IMyCubeGrid targetGrid, IMyCubeGrid myGrid)
        {
            var cGrid = myGrid as MyCubeGrid;
            foreach (var block in cGrid.GetFatBlocks())
            {
                IMyLargeTurretBase turret = block as IMyLargeTurretBase;
                if (turret != null && turret.IsWorking)
                {
                    turret.SetLockedTarget(targetGrid);
                    var ingameTurret = turret as Sandbox.ModAPI.Ingame.IMyLargeTurretBase;
                    if (ingameTurret != null)
                    {
                        //List<Sandbox.ModAPI.Interfaces.ITerminalAction> acts = new List<Sandbox.ModAPI.Interfaces.ITerminalAction>();
                        //ingameTurret.GetActions(acts);
                        var focusAct = ingameTurret.GetActionWithName("FocusLockedTarget");
                        if (focusAct != null)
                        {
                            focusAct.Apply(ingameTurret);
                        }
                    }
                    MyAPIGateway.Utilities.ShowMessage("", $"Locked to: {targetGrid.DisplayName}");
                }
            }
        }

        private void MoveToGridPosition(IMyFlightMovementBlock flightBlock, Vector3D adjustedPos)
        {
            try
            {
                MyAutopilotComponent comp = flightBlock.Components.Get<MyAutopilotComponent>();
                if (comp != null)
                {
                    comp.ClearWaypoints();
                    comp.AddWaypoint(adjustedPos, "", true);
                }
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLine($"KLIME RTS: {e.Message}");
            }
        }

        private void MoveToCharacterPosition(IMyCharacter character, Vector3D adjustedPos)
        {
            try
            {
                if (aiEnabledAPI == null || !aiEnabledAPI.Valid) return;

                aiEnabledAPI.SetBotTarget(character.EntityId, null);
                aiEnabledAPI.SetBotGoto(character.EntityId, adjustedPos);
                aiEnabledAPI.SetGotoRemovedAction(character.EntityId, BotArrived);
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLine($"KLIME RTS: {e.Message}");
            }
        }

        private void BotArrived(long botId, bool arg2)
        {
            try
            {
                if (aiEnabledAPI == null || !aiEnabledAPI.Valid) return;

                List<Vector3D> tmpWaypoint = new List<Vector3D>();
                var botEnt = MyAPIGateway.Entities.GetEntityById(botId) as IMyCharacter;
                if (botEnt != null)
                {
                    tmpWaypoint.Add(botEnt.GetPosition());
                }

                aiEnabledAPI.SetGotoRemovedAction(botId, null);
                aiEnabledAPI.ResetBotTargeting(botId);
                aiEnabledAPI.SetBotPatrol(botId, tmpWaypoint);

                foreach (var avChar in avaiableCharacters)
                {
                    if (avChar.character?.EntityId == botId)
                    {
                        avChar.currentMoveState = MoveState.Idle;
                    }
                }
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLine($"KLIME RTS: {e.Message}");
            }
        }

        public override void HandleInput()
        {
            if (MyAPIGateway.Utilities.IsDedicated)
            {
                return;
            }

            if (currentViewState == ViewState.InView)
            {
                var inputVec = MyAPIGateway.Input.GetPositionDelta();

                //Speed
                var finalScaling = spectatorScaling;
                if (MyAPIGateway.Input.IsAnyShiftKeyPressed())
                {
                    finalScaling *= 2.85714285714;
                }

                if (MyAPIGateway.Input.IsAnyCtrlKeyPressed())
                {
                    finalScaling *= 0.3;
                }

                if (!isRotating)
                {
                    if (validInputThisTick && inputVec.LengthSquared() > 0)
                    {
                        var oppInputVec = -1 * inputVec;
                        var cancelInputVec = oppInputVec.X * workingMatrix.Right + oppInputVec.Z * workingMatrix.Backward;
                        spectator.Position += cancelInputVec * spectator.SpeedModeLinear * finalScaling;
                    }
                }
            }
        }

        public override void UpdateAfterSimulation()
        {
            for (int i = delayGrids.Count - 1; i >= 0; i--)
            {
                if (masterTimer > delayGrids[i].runTime)
                {
                    ProcessGrid(delayGrids[i].grid);
                    delayGrids.RemoveAt(i);
                }
            }
            masterTimer++;
        }

        public void ProcessGrid(IMyCubeGrid grid)
        {
            currentOB = grid.GetObjectBuilder() as MyObjectBuilder_CubeGrid;

            var fakeOb = currentOB.Clone() as MyObjectBuilder_CubeGrid;
            MyAPIGateway.Entities.RemapObjectBuilder(fakeOb);
            fakeOb.EntityId = 0;
            fakeOb.CreatePhysics = false;
            MyAPIGateway.Entities.CreateFromObjectBuilderParallel(fakeOb, false, CompleteGrid);

            grid.Close();
        }

        private void CompleteGrid(IMyEntity entity)
        {
            fakeBuildGrid = entity as MyCubeGrid;
            fakeBuildGrid.SyncFlag = false;
            fakeBuildGrid.Save = false;
            fakeBuildGrid.RemoveFromGamePruningStructure();
            fakeBuildGrid.Render.CastShadows = false;
            fakeBuildGrid.DisplayName = "";
            
            var iGrid = fakeBuildGrid as IMyCubeGrid;
            List<IMySlimBlock> blocks = new List<IMySlimBlock>();
            iGrid.GetBlocks(blocks);
            foreach (var block in blocks)
            {
                block.Dithering = 2.5f;
            }

            MyAPIGateway.Entities.AddEntity(fakeBuildGrid);
        }

        public override void Draw()
        {
            if (MyAPIGateway.Utilities.IsDedicated)
            {
                return;
            }

            if (ValidateInput())
            {
                validInputThisTick = true;
            }
            else
            {
                validInputThisTick = false;
            }

            IMyCharacter charac = MyAPIGateway.Session?.Player?.Character;

            if (validInputThisTick && MyAPIGateway.Input.IsNewKeyPressed(MyKeys.T))
            {
                if (currentViewState == ViewState.Idle)
                {
                    if (ValidateLocalCharacter())
                    {
                        currentViewState = ViewState.GoToView;
                    }
                }
                else
                {
                    currentViewState = ViewState.GoToIdle;
                }
            }

            if (currentViewState == ViewState.Idle)
            {

            }

            if (currentViewState == ViewState.GoToView)
            {
                if (!MyAPIGateway.Session.IsCameraUserControlledSpectator)
                {
                    MyAPIGateway.Session.SetCameraController(MyCameraControllerEnum.Spectator, null, charac.WorldMatrix.Translation);
                    spectator = MyAPIGateway.Session.CameraController as MySpectator;
                    nearPlanet = MyGamePruningStructure.GetClosestPlanet(charac.WorldMatrix.Translation);
                    MyVisualScriptLogicProvider.SetHudState(0, 0);
                }

                if (spectator == null || nearPlanet == null)
                {
                    currentViewState = ViewState.GoToIdle;
                    return;
                }

                if (viewAnimFrame < maxViewAnimFrame)
                {
                    var direction = Vector3D.Normalize(charac.WorldMatrix.Backward + charac.WorldMatrix.Right + (charac.WorldMatrix.Up * 2));
                    var upVec = Vector3D.Normalize(charac.WorldMatrix.Forward + charac.WorldMatrix.Left + (charac.WorldMatrix.Up * 2));

                    var finalPosition = charac.WorldAABB.Center + (direction * pHeightDefault);
                    var realRatio = (double)viewAnimFrame / maxViewAnimFrame;
                    var easingRatio = OutQuint((float)realRatio);
                    spectator.SetTarget(charac.WorldAABB.Center, upVec);
                    spectator.Position = Vector3D.Lerp(charac.WorldAABB.Center, finalPosition, easingRatio);
                    viewAnimFrame += 1;
                }
                else
                {
                    freezeMatrix = MatrixD.CreateWorld(spectator.Position, spectator.Orientation.Forward, spectator.Orientation.Up);
                    workingMatrix = new MatrixD(freezeMatrix);
                    spectator.SpeedModeLinear = 1f;
                    MyVisualScriptLogicProvider.SetHudState(0, 0);
                    CreateElements();

                    currentViewState = ViewState.InView;
                }
            }

            if (currentViewState == ViewState.InView)
            {
                var planetCenter = nearPlanet.PositionComp.GetPosition();
                var fromPlanetVec = Vector3D.Normalize(spectator.Position - planetCenter);
                PlaneD surfacePlane = new PlaneD(spectator.Position, fromPlanetVec);
                bool needsMove = false;

                var inputVec = MyAPIGateway.Input.GetPositionDelta();

                //Rotation
                if (MyAPIGateway.Input.IsLeftMousePressed())
                {
                    if (MyAPIGateway.Input.IsKeyPress(MyKeys.R))
                    {
                        if (!isRotating)
                        {
                            isRotating = true;
                            rotPrev = mouse.Position;
                            rotVel = Vector2.Zero;
                        }

                        if (isRotating)
                        {
                            var rotCurrent = mouse.Position;
                            var rotDiff = rotCurrent - rotPrev;

                            if (rotDiff.LengthSquared() > 0)
                            {
                                rotVel += rotDiff;
                            }
                            else
                            {
                                rotVel = Vector2.Zero;
                            }


                            MatrixD xRotationMatrix = MatrixD.Identity;
                            MatrixD yRotationMatrix = MatrixD.Identity;

                            if (Math.Abs(rotVel.X) > 0)
                            {
                                var focusAxis = Vector3D.Normalize(workingFocus - nearPlanet.PositionComp.GetPosition());
                                xRotationMatrix = MatrixD.CreateFromAxisAngle(focusAxis, -1 * rotVel.X * 0.0005);
                            }

                            if (Math.Abs(rotVel.Y) > 0)
                            {
                                var focusAxis = workingMatrix.Right;
                                yRotationMatrix = MatrixD.CreateFromAxisAngle(focusAxis, rotVel.Y * 0.0005);
                            }


                            var focusRotationMatrix = xRotationMatrix * yRotationMatrix;

                            var fPos = workingFocus + Vector3D.Rotate(workingMatrix.Translation - workingFocus, focusRotationMatrix);
                            var fAxis = Vector3D.Normalize(fPos - nearPlanet.PositionComp.GetPosition());
                            var fForward = Vector3D.Normalize(workingFocus - fPos);

                            var fPoint = fPos + fForward;
                            var fPlane = new PlaneD(fPos, fAxis);
                            var fSurfaceForward = Vector3D.Normalize(fPos - fPlane.ProjectPoint(ref fPoint));

                            var fAngle = MyUtils.GetAngleBetweenVectors(fForward, fSurfaceForward);

                            if (fAngle > (Math.PI/2 + 0.1) && fAngle < 2.7)
                            {
                                var fUp = Vector3D.Normalize(RotateVectorTowards(fForward, fAxis, Math.PI / 2));
                                workingMatrix = MatrixD.CreateWorld(fPos, fForward, fUp);
                            }

                            rotPrev = mouse.Position;
                        }
                    }
                }

                
                //Movement
                Vector3D camForward = spectator.Position + workingMatrix.Forward;
                Vector3D camBackward = spectator.Position + workingMatrix.Backward;
                Vector3D surfaceForward = Vector3D.Normalize(surfacePlane.ProjectPoint(ref camForward) - spectator.Position);
                Vector3D surfaceBackward = Vector3D.Normalize(surfacePlane.ProjectPoint(ref camBackward) - spectator.Position);

                if (!isRotating)
                {
                    if (inputVec.LengthSquared() != 0 || (MyAPIGateway.Input.DeltaMouseScrollWheelValue() != 0))
                    {
                        workingCameraVelocity = Vector3D.Zero;
                    }

                    if (validInputThisTick && inputVec.LengthSquared() > 0)
                    {
                        workingCameraVelocity += inputVec.X * workingMatrix.Right + inputVec.Z * surfaceBackward;
                        needsMove = true;
                    }

                    if (mouse.Position.X < -959)
                    {
                        workingCameraVelocity = Vector3D.Zero;
                        workingCameraVelocity += workingMatrix.Left;
                        needsMove = true;
                    }

                    if (mouse.Position.X > 959)
                    {
                        workingCameraVelocity = Vector3D.Zero;
                        workingCameraVelocity += workingMatrix.Right;
                        needsMove = true;
                    }

                    if (mouse.Position.Y < -539)
                    {
                        workingCameraVelocity = Vector3D.Zero;
                        workingCameraVelocity += surfaceBackward;
                        needsMove = true;
                    }

                    if (mouse.Position.Y > 539)
                    {
                        workingCameraVelocity = Vector3D.Zero;
                        workingCameraVelocity += surfaceForward;
                        needsMove = true;
                    }

                    //Bottom left corner
                    if (mouse.Position.X < -959 && mouse.Position.Y < -539)
                    {
                        workingCameraVelocity = Vector3D.Zero;
                        workingCameraVelocity += workingMatrix.Left + surfaceBackward;
                        needsMove = true;
                    }

                    //Bottom right corner
                    if (mouse.Position.X > 959 && mouse.Position.Y < -539)
                    {
                        workingCameraVelocity = Vector3D.Zero;
                        workingCameraVelocity += workingMatrix.Right + surfaceBackward;
                        needsMove = true;
                    }

                    //Top left corner
                    if (mouse.Position.X < -959 && mouse.Position.Y > 539)
                    {
                        workingCameraVelocity = Vector3D.Zero;
                        workingCameraVelocity += workingMatrix.Left + surfaceForward;
                        needsMove = true;
                    }

                    //Top right corner
                    if (mouse.Position.X > 959 && mouse.Position.Y > 539)
                    {
                        workingCameraVelocity = Vector3D.Zero;
                        workingCameraVelocity += workingMatrix.Right + surfaceForward;
                        needsMove = true;
                    }
                }

                //Zero special
                if (inputVec.Y == 1 && validInputThisTick)
                {
                    workingMatrix = freezeMatrix;
                    workingCameraVelocity = Vector3D.Zero;
                    needsMove = true;
                }


                spectator.SpeedModeLinear = Math.Min(spectator.SpeedModeLinear, 50);

                Vector3D currentSurfacePos = nearPlanet.GetClosestSurfacePointGlobal(spectator.Position);
                var currentHeight = Vector3D.Distance(currentSurfacePos, workingMatrix.Translation);
                var heightAboveMin = currentHeight - pHeightMin;
                var heightDiff = pHeightMax - pHeightMin;
                var heightRatio = MathHelper.Lerp(0, 1, heightAboveMin / heightDiff);

                if (needsMove)
                {
                    if (workingCameraVelocity.Length() > 0)
                    {
                        var heightSpeed = 0.2 + (10 * heightRatio);
                        workingCameraVelocity = Vector3D.Normalize(workingCameraVelocity) * spectator.SpeedModeLinear * spectatorScaling * heightSpeed;
                    }
                }
                else
                {
                    workingCameraVelocity *= 0.9;
                    if (workingCameraVelocity.Length() < 0.1)
                    {
                        workingCameraVelocity = Vector3D.Zero;
                    }
                }

                if (!MyAPIGateway.Input.IsAnyShiftKeyPressed() && validInputThisTick)
                {
                    if (MyAPIGateway.Input.DeltaMouseScrollWheelValue() < 0)
                    {
                        workingMatrix.Translation += workingMatrix.Backward * (0.05 * heightDiff);
                    }

                    if (MyAPIGateway.Input.DeltaMouseScrollWheelValue() > 0)
                    {
                        workingMatrix.Translation += workingMatrix.Forward * (0.05 * heightDiff);
                    }
                }


                Vector3D finalPosition = workingMatrix.Translation + workingCameraVelocity;
                Vector3D finalGroundPos = nearPlanet.GetClosestSurfacePointGlobal(finalPosition);
                var finalHeight = Vector3D.Distance(finalGroundPos, finalPosition);

                if (finalHeight < pHeightMin)
                {
                    finalPosition += fromPlanetVec * (pHeightMin - finalHeight);
                }
                else if (finalHeight > pHeightMax)
                {
                    finalPosition += -1 * fromPlanetVec * (finalHeight - pHeightMax);
                }

                spectator.Position = finalPosition + workingCameraVelocity;
                workingMatrix.Translation = spectator.Position;

                ComputeWorkingDistance();
                spectator.SetTarget(workingFocus, workingMatrix.Up);
                //MyAPIGateway.Utilities.ShowNotification($"Working focus: {workingFocus}", 16, "White");
                if (!MyAPIGateway.Session.IsCameraUserControlledSpectator)
                {
                    MyAPIGateway.Session.SetCameraController(MyCameraControllerEnum.Spectator, null);
                }
            }

            if (currentViewState == ViewState.GoToIdle)
            {
                RemoveElements();
                RemoveBuildElements();
                MyAPIGateway.Session.SetCameraController(MyCameraControllerEnum.ThirdPersonSpectator, MyAPIGateway.Session.Player.Character);
                MyVisualScriptLogicProvider.SetHudState(1, 0);

                currentViewState = ViewState.Idle;
                viewAnimFrame = 0;
                freezeMatrix = MatrixD.Identity;
                workingMatrix = MatrixD.Identity;
                workingCameraVelocity = Vector3D.Zero;
                isRotating = false;
                rotPrev = Vector2.Zero;
                rotVel = Vector2.Zero;
            }

            if (validInputThisTick && MyAPIGateway.Input.IsNewKeyPressed(MyKeys.B) && currentViewState == ViewState.InView)
            {
                if (currentBuildState == BuildState.Idle)
                {
                    if (ValidateLocalCharacter())
                    {
                        currentBuildState = BuildState.GoToPicker;
                    }
                }
                else
                {
                    currentBuildState = BuildState.GoToIdle;
                }
            }

            if (currentBuildState == BuildState.Idle)
            {

            }

            if (currentBuildState == BuildState.GoToPicker)
            {
                CreateBuildElements();
                currentBuildState = BuildState.InPicker;
            }

            if (currentBuildState == BuildState.InPicker)
            {

            }

            if (currentBuildState == BuildState.GoToBuild)
            {
                MyAPIGateway.PrefabManager.SpawnPrefab(incomingPrefab, currentBuildGridName, Vector3D.Zero + Vector3D.Forward * 100, Vector3D.Forward, Vector3D.Up,
                    ownerId: MyAPIGateway.Session.Player.IdentityId);
                currentBuildState = BuildState.InBuild;
            }

            if (currentBuildState == BuildState.InBuild)
            {
                if (fakeBuildGrid != null)
                {
                    Vector3D groundPos = GetBuildGroundPos();
                    Vector3D groudUp = Vector3D.Normalize(groundPos - nearPlanet.PositionComp.GetPosition());
                    Vector3D groundForward = Vector3D.Normalize(Vector3D.CalculatePerpendicularVector(groudUp));

                    fakeBuildGrid.WorldMatrix = MatrixD.CreateWorld(groundPos, -groudUp, groundForward);

                    if (MyAPIGateway.Input.IsNewLeftMousePressed())
                    {
                        currentBuildState = BuildState.GoToCreate;
                    }
                }
            }

            if (currentBuildState == BuildState.GoToCreate)
            {
                var realOB = currentOB.Clone() as MyObjectBuilder_CubeGrid;
                MyAPIGateway.Entities.RemapObjectBuilder(realOB);
                realOB.EntityId = 0;
                realOB.PositionAndOrientation = new MyPositionAndOrientation(fakeBuildGrid.WorldMatrix);

                fakeBuildGrid.Close();
                fakeBuildGrid = null;

                MyAPIGateway.Entities.CreateFromObjectBuilderParallel(realOB, false, CreateGrid);

                currentBuildState = BuildState.LoopCreate;
            }

            if (currentBuildState == BuildState.LoopCreate)
            {
                currentBuildGridName = "";
                fakeBuildGrid?.Close();
                fakeBuildGrid = null;

                incomingPrefab.Clear();
                currentOB = null;
                currentBuildState = BuildState.InPicker;
            }

            if (currentBuildState == BuildState.GoToIdle)
            {
                RemoveBuildElements();
                currentBuildState = BuildState.Idle;
            }

            if (currentBuildUI != null)
            {
                currentBuildUI?.Update(mouse.Position);
            }
        }

        private void CreateGrid(IMyEntity entity)
        {
            var grid = entity as MyCubeGrid;
            MyAPIGateway.Entities.AddEntity(grid);
        }

        private Vector3D GetBuildGroundPos()
        {
            Vector3D worldPos = new Vector3D(mouse.Position.X, mouse.Position.Y, -0.00000001);
            worldPos = Vector3D.Transform(worldPos, mouse.HudSpace.PlaneToWorld);
            var camPos = MyAPIGateway.Session.Camera.WorldMatrix.Translation;
            var dirFromCam = Vector3D.Normalize(worldPos - camPos);

            hits.Clear();
            MyAPIGateway.Physics.CastRay(camPos, camPos + (dirFromCam * 1000), hits);

            Vector3D groundRetVec = Vector3D.Zero;
            if (hits.Count > 0)
            {
                hits.OrderBy(x => Vector3D.Distance(worldPos, x.Position));
                IHitInfo selectedHit = null;

                foreach (var hit in hits)
                {
                    if (hit.HitEntity != null)
                    {
                        MyVoxelBase voxel = hit.HitEntity as MyVoxelBase;
                        if (voxel != null)
                        {
                            selectedHit = hit;
                            break;
                        }
                    }
                }

                if (selectedHit != null)
                {
                    groundRetVec = selectedHit.Position;
                }
            }

            return groundRetVec;
        }

        private void CreateElements()
        {
            mouse = new CustomMouse();

            searchEnts.Clear();
            var center = MyAPIGateway.Session.Player.Character.WorldMatrix.Translation;
            var sphere = new BoundingSphereD(center, 20000);
            MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, searchEnts);

            foreach (var ent in searchEnts)
            {
                MyCubeGrid grid = ent as MyCubeGrid;
                if (grid != null)
                {
                    if (ValidateGrid(grid))
                    {
                        var gridRender = new RTSGrid(grid);
                        gridRender.Register(HudMain.HighDpiRoot, true);
                        availableGrids.Add(gridRender);
                    }
                }

                IMyCharacter characterBot = ent as IMyCharacter;
                if (characterBot != null)
                {
                    if (ValidateBot(characterBot))
                    {
                        var characterRender = new RTSCharacter(characterBot);
                        characterRender.Register(HudMain.HighDpiRoot, true);
                        avaiableCharacters.Add(characterRender);
                    }
                }
            }
        }

        private void RemoveElements()
        {
            try
            {
                mouse?.Close();
                mouse = null;

                //Available grids
                foreach (var avGrid in availableGrids)
                {
                    avGrid.Close();
                }
                availableGrids.Clear();

                foreach (var avChar in avaiableCharacters)
                {
                    avChar.Close();
                }
                avaiableCharacters.Clear();
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLine($"KLIME RTS: {e.Message}");
            }
        }

        private void CreateBuildElements()
        {
            currentBuildUI = new BuildUI(buildPrefabNames);
        }

        private void RemoveBuildElements()
        {
            currentBuildUI?.Close();
            currentBuildUI = null;

            currentBuildGridName = "";
            fakeBuildGrid?.Close();
            fakeBuildGrid = null;

            incomingPrefab.Clear();
            currentOB = null;
        }

        public void BuildRequest(string buildName)
        {
            currentBuildGridName = buildName;
            if (currentBuildState == BuildState.InPicker)
            {
                currentBuildState = BuildState.GoToBuild;
            }
        }

        private void ComputeWorkingDistance()
        {
            if (isRotating) return;

            workingHits.Clear();

            var camMat = MyAPIGateway.Session.Camera.WorldMatrix;
            MyAPIGateway.Physics.CastRay(camMat.Translation, camMat.Translation + (camMat.Forward * 1000), workingHits);

            MyVoxelBase hitVoxel = null;
            Vector3D hitVoxelPos = Vector3D.Zero;
            foreach (var hit in workingHits)
            {
                if (hit != null && hit.HitEntity != null && hit.HitEntity is MyVoxelBase)
                {
                    hitVoxel = hit.HitEntity as MyVoxelBase;
                    hitVoxelPos = hit.Position;
                    break;
                }
            }

            double dist = 1000;
            if (hitVoxel != null)
            {
                dist = Vector3D.Distance(hitVoxelPos, camMat.Translation);
            }

            workingFocus = spectator.Position + (workingMatrix.Forward * dist);
        }

        private bool ValidateInput()
        {
            if (MyAPIGateway.Session.CameraController != null && !MyAPIGateway.Gui.ChatEntryVisible && !MyAPIGateway.Gui.IsCursorVisible
                && MyAPIGateway.Gui.GetCurrentScreen == MyTerminalPageEnum.None)
            {
                return true;
            }
            return false;
        }

        private bool ValidateLocalCharacter()
        {
            if (!(MyAPIGateway.Session?.ControlledObject?.Entity is IMyCharacter))
            {
                return false;
            }

            var charac = MyAPIGateway.Session?.Player?.Character;
            if (charac == null || charac.IsDead)
            {
                return false;
            }

            var nearPlanet = MyGamePruningStructure.GetClosestPlanet(charac.WorldMatrix.Translation);
            if (nearPlanet == null)
            {
                return false;
            }

            //var distance = Vector3D.Distance(nearPlanet.GetClosestSurfacePointGlobal(charac.WorldMatrix.Translation), charac.WorldMatrix.Translation);
            //if (distance > 50)
            //{
            //    return false;
            //}

            return true;
        }

        public Vector3D RotateVectorTowards(Vector3D source, Vector3D target, double angleInRadians)
        {
            // Calculate the axis of rotation
            Vector3D rotationAxis = Vector3D.Normalize(Vector3D.Cross(source, target));

            // Ensure the rotationAxis is not a zero vector
            if (rotationAxis.LengthSquared() == 0)
            {
                // Source and target vectors are parallel, no rotation needed
                return source;
            }

            // Calculate the angle between source and target vectors
            double cosTheta = Vector3D.Dot(source, target) / (source.Length() * target.Length());

            // Clamp the cosTheta value between -1 and 1 to avoid issues with the acos function
            cosTheta = MathHelper.Clamp(cosTheta, -1, 1);

            // Calculate the actual angle between source and target vectors
            double actualAngle = Math.Acos(cosTheta);

            // Clamp the input angle between 0 and the actual angle between source and target vectors
            angleInRadians = MathHelper.Clamp(angleInRadians, 0, actualAngle);

            // Create a rotation matrix for the given angle around the rotation axis
            MatrixD rotationMatrix = MatrixD.CreateFromAxisAngle(rotationAxis, angleInRadians);

            // Rotate the source vector using the rotation matrix and return the result
            return Vector3D.TransformNormal(source, rotationMatrix);
        }

        private bool ValidateGrid(MyCubeGrid grid)
        {
            searchBlocks.Clear();

            if (grid == null || grid.Physics == null || grid.IsStatic)
            {
                return false;
            }

            MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid).GetBlocksOfType<IMyBasicMissionBlock>(searchBlocks);
            if (searchBlocks.Count == 0)
            {
                return false;
            }

            IMyBasicMissionBlock mBlock = (IMyBasicMissionBlock)searchBlocks[0];
            if (mBlock == null)
            {
                return false;
            }

            var relation = MyIDModule.GetRelationPlayerBlock(mBlock.OwnerId, MyAPIGateway.Session.Player.IdentityId);
            if (relation == MyRelationsBetweenPlayerAndBlock.Enemies)
            {
                return false;
            }
            return true;
        }

        private bool ValidateBot(IMyCharacter characterBot)
        {
            if (characterBot == null || characterBot.IsDead || MyAPIGateway.Session.Player == null)
            {
                return false;
            }

            //Only AiEnabled bots
            if (!string.IsNullOrWhiteSpace(characterBot.DisplayName))
            {
                return false;
            }

            var relation = MyIDModule.GetRelationPlayerPlayer(characterBot.ControllerInfo.ControllingIdentityId, MyAPIGateway.Session.Player.IdentityId);
            if (relation == MyRelationsBetweenPlayers.Enemies)
            {
                return false;
            }

            return true;
        }

        private void InitComplete()
        {

        }

        private void ClientReset()
        {

        }

        protected override void UnloadData()
        {
            rtsInstance = null;

            if (MyAPIGateway.Session.IsServer)
            {
                MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(netId, NetworkHandler);
                aiEnabledAPI?.Close();
            }

            MyVisualScriptLogicProvider.PrefabSpawnedDetailed -= PrefabSpawned;
        }
    }
}