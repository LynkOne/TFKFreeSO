﻿/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FSO.Client.UI.Framework;
using FSO.Client.UI.Panels;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Model;
using FSO.Client.Rendering.City;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FSO.Client.Utils;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Rendering.Framework.IO;
using FSO.Common.Rendering.Framework;
using FSO.Files.Formats.IFF.Chunks;
using FSO.HIT;

using FSO.LotView;
using FSO.SimAntics;
using FSO.LotView.Components;
using FSO.Client.UI.Panels.LotControls;
using Microsoft.Xna.Framework.Input;
using FSO.LotView.Model;
using FSO.SimAntics.Primitives;
using FSO.SimAntics.NetPlay.Model.Commands;
using FSO.Client.Debug;
using FSO.SimAntics.NetPlay.Model;
using FSO.SimAntics.Model.TSOPlatform;
using FSO.Client.UI.Panels.EODs;
using FSO.SimAntics.Utils;
using FSO.Common;
using FSO.LotView.RC;
using System.IO;
using FSO.SimAntics.Engine.TSOTransaction;
using FSO.LotView.Facade;
using FSO.Common.Enum;
using FSO.Client.UI.Screens;
using Ninject;
using FSO.Client.Network;
using FSO.Client.UI.Panels.Neighborhoods;
using FSO.UI.Controls;
using FSO.Client.UI.Panels.Profile;
using FSO.SimAntics.Model;
using FSO.Common.Utils;
using FSO.SimAntics.Engine;

namespace FSO.Client.UI.Panels
{
    /// <summary>
    /// Generates pie menus when the player clicks on objects.
    /// </summary>
    public class UILotControl : UIContainer, IDisposable, ITouchable, IFocusableUI
    {
        private UIMouseEventRef MouseEvt;
        public bool MouseIsOn;

        private UIPieMenu PieMenu;
        public UIChatPanel ChatPanel;
        private UIAlert LotSaveDialog;
        private bool HasInitUserProps;

        private bool ShowTooltip;
        private bool TipIsError;
        private Texture2D RMBCursor;

        public FSO.SimAntics.VM vm;
        public LotView.World World;
        public VMEntity ActiveEntity;
        public uint Budget
        {
            get
            {
                if (ActiveEntity == null) return uint.MaxValue;
                return ActiveEntity.TSOState.Budget.Value;
            }
        }
        public uint SelectedSimID {
            get
            {
                return (vm == null) ? 0 : vm.MyUID;
            }
        }
        public short ObjectHover;
        public bool InteractionsAvailable;
        public UIInteractionQueue Queue;

        public bool LiveMode = true;
        public bool PanelActive = false;
        public UILotControlTouchHelper Touch;

        public UIObjectHolder ObjectHolder;
        public UIQueryPanel QueryPanel;
        public UIDonatorDialog DonatorDialog;

        public UICustomLotControl CustomControl;
        public UIEODController EODs;

        public int WallsMode = 1;

        private int OldMX;
        private int OldMY;
        private bool FoundMe; //if false and avatar changes, center. Should center on join lot.

        public bool KBScroll;

        public bool RMBScroll;
        private int RMBScrollX;
        private int RMBScrollY;

        private bool LastFirstPerson;
        private bool FirstPerson = true;
        private int FirstPersonID = 0;
        private float FirstPersonSinceUpdate = 0f;

        public UICheatHandler Cheats;
        public UIAvatarDataServiceUpdater AvatarDS;

        //1 = near, 0.5 = med, 0.25 = far
        //"target" because we rescale the game target to fit this zoom level.
        public float TargetZoom { get; set; } = 1;

        public float BBScale { get { return World.BackbufferScale; } }
        public I3DRotate Rotate { get { return World.State.Cameras.Camera3D; } } //(I3DRotate)World.State; } }
        public bool TVisible { get { return Visible; } }
        public bool UserModZoom { get; set; }

        public void Scroll(Vector2 vec)
        {
            World.Scroll(vec, false);
        }

        // NOTE: Blocking dialog system assumes that nothing goes wrong with data transmission (which it shouldn't, because we're using TCP)
        // and that the code actually blocks further dialogs from appearing while waiting for a response.
        // If we are to implement controlling multiple sims, this must be changed.
        private UIAlert BlockingDialog;
        private UIAlert DialogTakeFocus;
        private UINeighborhoodSelectionPanel TS1NeighSelector;
        private ulong LastDialogID;

        private static uint GOTO_GUID = 0x000007C4;
        public VMEntity GotoObject;

        private Rectangle MouseCutRect = new Rectangle(-4, -4, 4, 4);
        private List<uint> CutRooms = new List<uint>();
        private HashSet<uint> LastCutRooms = new HashSet<uint>(); //final rooms, including those outside. used to detect dirty.
        public sbyte LastFloor = -1;
        public WorldRotation LastRotation = WorldRotation.TopLeft;
        private bool[] LastCuts; //cached roomcuts, to apply rect cut to.
        private int LastWallMode = -1; //invalidates last roomcuts
        private bool LastRectCutNotable = false; //set if the last rect cut made a noticable change to the cuts array. If true refresh regardless of new cut effect.
        private bool HasLanded = false;

        /// <summary>
        /// Creates a new UILotControl instance.
        /// </summary>
        /// <param name="vm">A SimAntics VM instance.</param>
        /// <param name="World">A World instance.</param>
        public UILotControl(FSO.SimAntics.VM vm, LotView.World World)
        {
            this.vm = vm;
            this.World = World;

            ActiveEntity = vm.Entities.FirstOrDefault(x => x is VMAvatar);
            MouseEvt = this.ListenForMouse(new Microsoft.Xna.Framework.Rectangle(0, 0, 
                GlobalSettings.Default.GraphicsWidth, GlobalSettings.Default.GraphicsHeight), OnMouse);

            Queue = new UIInteractionQueue(ActiveEntity, vm);
            this.Add(Queue);

            ObjectHolder = new UIObjectHolder(vm, World, this);
            Touch = new UILotControlTouchHelper(this);
            Add(Touch);
            SetupQuery();

            ChatPanel = new UIChatPanel(vm, this);
            this.Add(ChatPanel);

            RMBCursor = GetTexture(0x24B00000001); //exploreanchor.bmp

            vm.OnChatEvent += Vm_OnChatEvent;
            vm.OnDialog += vm_OnDialog;
            vm.OnBreakpoint += Vm_OnBreakpoint;

            Cheats = new UICheatHandler(this);
            this.Add(Cheats);
            AvatarDS = new UIAvatarDataServiceUpdater(this);
            EODs = new UIEODController(this);
            this.Add(DonatorDialog);
        }

        public void SetDonatorDialogVisible(bool visible)
        {
            if (vm.TSOState?.CommunityLot == true)
            {
                if (DonatorDialog == null)
                {
                    if (!visible) return;
                    DonatorDialog = new UIDonatorDialog(this);
                    Add(DonatorDialog);
                }
                DonatorDialog.Visible = visible;
            }
        }

        public void SetupQuery()
        {
            UIContainer parent = null;
            if (QueryPanel?.Parent?.Parent != null)
            {
                parent = QueryPanel.Parent;
            }

            QueryPanel = new UIQueryPanel(this, World);
            QueryPanel.OnSellBackClicked += ObjectHolder.SellBack;
            QueryPanel.OnInventoryClicked += ObjectHolder.MoveToInventory;
            QueryPanel.OnAsyncBuyClicked += ObjectHolder.AsyncBuy;
            QueryPanel.OnAsyncSaleClicked += ObjectHolder.AsyncSale;
            QueryPanel.OnAsyncPriceClicked += ObjectHolder.AsyncSale;
            QueryPanel.OnAsyncSaleCancelClicked += ObjectHolder.AsyncCancelSale;
            QueryPanel.X = 0;
            QueryPanel.Y = -114;

            if (parent != null) parent.Add(QueryPanel);
        }

        public override void GameResized()
        {
            base.GameResized();
            MouseEvt.Region.Width = GlobalSettings.Default.GraphicsWidth;
            MouseEvt.Region.Height = GlobalSettings.Default.GraphicsHeight;

            SetupQuery();
        }

        private void Vm_OnChatEvent(VMChatEvent evt)
        {
            UpdateChatTitle();
            if (evt.Type == VMChatEventType.Message && evt.SenderUID == SelectedSimID) evt.Type = VMChatEventType.MessageMe;
            ChatPanel.ReceiveEvent(evt);
        }

        private int LastVisitorCount = 0;
        private void UpdateChatTitle()
        {
            var visitors = vm.Context.ObjectQueries.Avatars.Count(x => x.PersistID != 0);
            if (LastVisitorCount != visitors)
            {
                ChatPanel.SetVisitorCount(visitors);
                ChatPanel.SetLotName(vm.LotName);
                LastVisitorCount = visitors;
            }
        }

        private void Vm_OnBreakpoint(VMEntity entity)
        {
            if (IDEHook.IDE != null) IDEHook.IDE.IDEBreakpointHit(vm, entity);
        }

        public string GetLotTitle()
        {
            return vm.LotName + " - " + vm.Entities.Count(x => x is VMAvatar && x.PersistID != 0);
        }

        void vm_OnDialog(FSO.SimAntics.Model.VMDialogInfo info)
        {
            if (info != null && ((info.DialogID == LastDialogID && info.DialogID != 0 && info.Block)
                || info.Caller != null && info.Caller != ActiveEntity)) return;
            //return if same dialog as before, or not ours
            if ((info == null || info.Block) && BlockingDialog != null)
            {
                //cancel current dialog because it's no longer valid
                UIScreen.RemoveDialog(BlockingDialog);
                LastDialogID = 0;
                BlockingDialog = null;
            }
            if (info == null) return; //return if we're just clearing a dialog.

            var options = new UIAlertOptions {
                Title = info.Title,
                Message = info.Message,
                Width = 325 + (int)(info.Message.Length / 3.5f),
                Alignment = TextAlignment.Left,
                TextSize = 12,
                AllowEmojis = true
            };

            if (info.Block && vm.TS1) vm.SpeedMultiplier = 0;
            var b0Event = (info.Block) ? new ButtonClickDelegate(DialogButton0) : null;
            var b1Event = (info.Block) ? new ButtonClickDelegate(DialogButton1) : null;
            var b2Event = (info.Block) ? new ButtonClickDelegate(DialogButton2) : null;

            VMDialogType type = (info.Operand == null) ? VMDialogType.Message : info.Operand.Type;

            switch (type)
            {
                default:
                case VMDialogType.Message:
                    options.Buttons = new UIAlertButton[] { new UIAlertButton(UIAlertButtonType.OK, b0Event, info.Yes) };
                    break;
                case VMDialogType.YesNo:
                    options.Buttons = new UIAlertButton[]
                    {
                        new UIAlertButton(UIAlertButtonType.Yes, b0Event, info.Yes),
                        new UIAlertButton(UIAlertButtonType.No, b1Event, info.No),
                    };
                    break;
                case VMDialogType.YesNoCancel:
                    options.Buttons = new UIAlertButton[]
                    {
                        new UIAlertButton(UIAlertButtonType.Yes, b0Event, info.Yes),
                        new UIAlertButton(UIAlertButtonType.No, b1Event, info.No),
                        new UIAlertButton(UIAlertButtonType.Cancel, b2Event, info.Cancel),
                    };
                    break;
                case VMDialogType.TextEntry:
                case VMDialogType.FSOChars:
                    options.Buttons = new UIAlertButton[] { new UIAlertButton(UIAlertButtonType.OK, b0Event, info.Yes) };
                    if (type == VMDialogType.FSOChars) options.MaxChars = 99999;
                    options.TextEntry = true;
                    break;
                case VMDialogType.NumericEntry:
                    if (!vm.TS1) goto case VMDialogType.TextEntry;
                    else goto case VMDialogType.TS1Neighborhood;
                case VMDialogType.TS1Vacation:
                case VMDialogType.TS1Neighborhood:
                case VMDialogType.TS1StudioTown:
                case VMDialogType.TS1Magictown:
                    TS1NeighSelector = new UINeighborhoodSelectionPanel((ushort)VMDialogPrivateStrings.TypeToNeighID[type]);
                    Parent.Add(TS1NeighSelector);
                    TS1NeighSelector.OnHouseSelect += HouseSelected;
                    return;
                case VMDialogType.FSOColor:
                    options.Buttons = new UIAlertButton[] { new UIAlertButton(UIAlertButtonType.OK, b0Event, info.Yes), new UIAlertButton(UIAlertButtonType.Cancel, b1Event, info.Cancel) };
                    options.GenericAddition = new UIColorPicker();
                    break;
                case VMDialogType.FSOJob:
                    VMAvatar avatar = (VMAvatar)info.Caller;
                    JobInformation JobInfo = new JobInformation((int)avatar.GetPersonData(VMPersonDataVariable.OnlineJobGrade), (int)avatar.GetPersonData(VMPersonDataVariable.OnlineJobID), (int)avatar.GetPersonData(VMPersonDataVariable.OnlineJobXP));
                    var jobInfoAlert = new UIJobInfo(JobInfo);
                    jobInfoAlert.Show();
                    return;
            }

            var alert = UIScreen.GlobalShowAlert(options, false);

            if (info.Block)
            {
                BlockingDialog = alert;
                LastDialogID = info.DialogID;
            }

            DialogTakeFocus = alert;

            var entity = info.Icon;
            if (entity is VMGameObject)
            {
                var objects = entity.MultitileGroup.Objects;
                ObjectComponent[] objComps = new ObjectComponent[objects.Count];
                for (int i = 0; i < objects.Count; i++)
                {
                    objComps[i] = (ObjectComponent)objects[i].WorldUI;
                }
                var thumb = World.GetObjectThumb(objComps, entity.MultitileGroup.GetBasePositions(), GameFacade.GraphicsDevice);
                alert.SetIcon(thumb, 110, 110);
            }
        }

        private void HouseSelected(int house)
        {
            if (ActiveEntity == null || TS1NeighSelector == null) return;
            vm.SendCommand(new VMNetDialogResponseCmd
            {
                ActorUID = ActiveEntity.PersistID,
                ResponseCode = (byte)((house > 0) ? 1 : 0),
                ResponseText = house.ToString()
            });
            Parent.Remove(TS1NeighSelector);
            if (vm.SpeedMultiplier == 0) vm.SpeedMultiplier = 1;
            TS1NeighSelector = null;
        }

        private void DialogButton0(UIElement button) { DialogResponse(0); }
        private void DialogButton1(UIElement button) { DialogResponse(1); }
        private void DialogButton2(UIElement button) { DialogResponse(2); }

        private void DialogResponse(byte code)
        {
            if (BlockingDialog == null || ActiveEntity == null) return;
            UIScreen.RemoveDialog(BlockingDialog);
            LastDialogID = 0;
            vm.SendCommand(new VMNetDialogResponseCmd {
                ActorUID = ActiveEntity.PersistID,
                ResponseCode = code,
                ResponseText = (BlockingDialog.ResponseText == null) ? "" : BlockingDialog.ResponseText
            });
            if (vm.SpeedMultiplier == 0) vm.SpeedMultiplier = 1;
            BlockingDialog = null;
        }

        private void OnMouse(UIMouseEventType type, UpdateState state)
        {
            if (!vm.Ready) return;

            if (type == UIMouseEventType.MouseOver)
            {
                if (QueryPanel.Mode == 1) QueryPanel.Active = false;
                MouseIsOn = true;
            }
            else if (type == UIMouseEventType.MouseOut)
            {
                MouseIsOn = false;
                GameFacade.Cursor.SetCursor(CursorType.Normal);
                Tooltip = null;
            }
            else if (type == UIMouseEventType.MouseDown)
            {
                Touch.MiceDown.Add(state.CurrentMouseID);
                state.InputManager.SetFocus(null);
            }
            else if (type == UIMouseEventType.MouseUp)
            {
                Touch.MiceDown.Remove(state.CurrentMouseID);
                if (!LiveMode)
                {
                    if (CustomControl != null) CustomControl.MouseUp(state);
                    else ObjectHolder.MouseUp(state);
                    return;
                }
                state.UIState.TooltipProperties.Show = false;
                state.UIState.TooltipProperties.Opacity = 0;
                ShowTooltip = false;
                TipIsError = false;
            }
        }

        private short GetFloorBlockableHover(Point pt)
        {
            var tilePos = World.EstTileAtPosWithScroll3D(new Vector2(pt.X, pt.Y));
            var newHover = World.GetObjectIDAtScreenPos(pt.X,
                    pt.Y,
                    GameFacade.GraphicsDevice);

            var hobj = vm.GetObjectById(newHover);
            if (hobj == null || hobj.Position.Level < tilePos.Z) newHover = 0;
            return newHover;
        }

        public void Click(Point pt, UpdateState state)
        {
            if (!LiveMode)
            {
                if (CustomControl != null) CustomControl.MouseDown(state);
                else ObjectHolder.MouseDown(state);
                return;
            }
            if (PieMenu == null && ActiveEntity != null)
            {
                VMEntity obj;
                //get new pie menu, make new pie menu panel for it
                var tilePos = World.EstTileAtPosWithScroll3D(new Vector2(pt.X, pt.Y));

                LotTilePos targetPos = LotTilePos.FromBigTile((short)tilePos.X, (short)tilePos.Y, (sbyte)tilePos.Z);
                if (vm.Context.SolidToAvatars(targetPos).Solid) targetPos = LotTilePos.OUT_OF_WORLD;

                GotoObject.SetPosition(targetPos, Direction.NORTH, vm.Context);

                var newHover = World.GetObjectIDAtScreenPos(pt.X,
                    pt.Y,
                    GameFacade.GraphicsDevice);

                var hobj = vm.GetObjectById(newHover);
                if (hobj == null || hobj.Position.Level < tilePos.Z) newHover = 0;
                ObjectHover = newHover;

                bool objSelected = ObjectHover > 0;
                if (objSelected || (GotoObject.Position != LotTilePos.OUT_OF_WORLD && ObjectHover <= 0))
                {
                    if (objSelected)
                    {
                        obj = vm.GetObjectById(ObjectHover);
                    }
                    else
                    {
                        obj = GotoObject;
                    }
                    if (obj != null)
                    {
                        obj = obj.MultitileGroup.GetInteractionGroupLeader(obj);
                        /*
                        if (state.CtrlDown && state.ShiftDown)
                        {
                            ActiveEntity = obj;
                            vm.MyUID = obj.PersistID;
                            Queue.QueueOwner = ActiveEntity;
                            Queue.DebugMode = true;
                        }*/
                        if (obj is VMGameObject && ((VMGameObject)obj).Disabled > 0)
                        {
                            var flags = ((VMGameObject)obj).Disabled;

                            if ((flags & VMGameObjectDisableFlags.ForSale) > 0)
                            {
                                //for sale
                                //try to get catalog price
                                var guid = obj.MasterDefinition?.GUID ?? obj.Object.OBJ.GUID;
                                var item = Content.Content.Get().WorldCatalog.GetItemByGUID(guid);

                                var retailPrice = (int?)(item?.Price) ?? obj.MultitileGroup.Price;
                                var salePrice = obj.MultitileGroup.SalePrice;
                                ShowErrorTooltip(state, 22, true, "$" + retailPrice.ToString("##,#0"), "$" + salePrice.ToString("##,#0"));
                            }
                            else if ((flags & VMGameObjectDisableFlags.LotCategoryWrong) > 0)
                                ShowErrorTooltip(state, 21, true); //category wrong
                            else if ((flags & VMGameObjectDisableFlags.TransactionIncomplete) > 0)
                                ShowErrorTooltip(state, 27, true); //transaction not yet complete
                            else if ((flags & VMGameObjectDisableFlags.ObjectLimitExceeded) > 0)
                                ShowErrorTooltip(state, 24, true); //object is temporarily disabled... todo: something more helpful
                            else if ((flags & VMGameObjectDisableFlags.PendingRoommateDeletion) > 0)
                                ShowErrorTooltip(state, 16, true); //pending roommate deletion
                        }
                        else
                        {
                            var menu = obj.GetPieMenu(vm, ActiveEntity, false, true);
                            if (menu.Count != 0)
                            {
                                HITVM.Get().PlaySoundEvent(UISounds.PieMenuAppear);
                                PieMenu = new UIPieMenu(menu, obj, ActiveEntity, this);
                                this.Add(PieMenu);
                                PieMenu.X = state.MouseState.X / FSOEnvironment.DPIScaleFactor;
                                PieMenu.Y = state.MouseState.Y / FSOEnvironment.DPIScaleFactor;
                                PieMenu.UpdateHeadPosition(state.MouseState.X, state.MouseState.Y);
                            } else
                            {
                                ShowErrorTooltip(state, 0, true);
                            }
                        }
                    }

                }
                else
                {
                    ShowErrorTooltip(state, 0, true);
                }
            }
            else
            {
                if (PieMenu != null) PieMenu.RemoveSimScene();
                this.Remove(PieMenu);
                PieMenu = null;
            }
        }

        private void ShowErrorTooltip(UpdateState state, uint id, bool playSound, params string[] args)
        {
            if (playSound) HITVM.Get().PlaySoundEvent(UISounds.Error);
            state.UIState.TooltipProperties.Show = true;
            state.UIState.TooltipProperties.Color = Color.Black;
            state.UIState.TooltipProperties.Opacity = 1;
            state.UIState.TooltipProperties.Position = new Vector2(state.MouseState.X,
                state.MouseState.Y);
            state.UIState.Tooltip = GameFacade.Strings.GetString("159", id.ToString(), args);
            state.UIState.TooltipProperties.UpdateDead = false;
            ShowTooltip = true;
            TipIsError = true;
        }

        public void ClosePie() 
        {
            if (PieMenu != null) 
            {
                PieMenu.RemoveSimScene();
                Queue.PieMenuClickPos = PieMenu.Position;
                this.Remove(PieMenu);
                PieMenu = null;
            }
        }

        public override Rectangle GetBounds()
        {
            return new Rectangle(0, 0, GlobalSettings.Default.GraphicsWidth, GlobalSettings.Default.GraphicsHeight);
        }

        public Point GetScaledPoint(Point TapPoint)
        {
            var screenMiddle = new Point(
                (int)(GameFacade.Screens.CurrentUIScreen.ScreenWidth / (2 / FSOEnvironment.DPIScaleFactor)),
                (int)(GameFacade.Screens.CurrentUIScreen.ScreenHeight / (2 / FSOEnvironment.DPIScaleFactor))
                );
            return ((TapPoint - screenMiddle).ToVector2() / World.BackbufferScale).ToPoint() + screenMiddle;
        }

        public void LiveModeUpdate(UpdateState state, bool scrolled)
        {
            if (MouseIsOn && !RMBScroll && ActiveEntity != null)
            {

                if (state.MouseState.X != OldMX || state.MouseState.Y != OldMY)
                {
                    OldMX = state.MouseState.X;
                    OldMY = state.MouseState.Y;
                    var scaled = GetScaledPoint(state.MouseState.Position);
                    var newHover = GetFloorBlockableHover(scaled);

                    if (ObjectHover != newHover)
                    {
                        ObjectHover = newHover;
                        if (ObjectHover > 0)
                        {
                            var obj = vm.GetObjectById(ObjectHover);
                            if (obj != null)
                            {
                                var menu = obj.GetPieMenu(vm, ActiveEntity, false, true);
                                InteractionsAvailable = (menu.Count > 0);
                            }
                        }
                    }

                    if (!TipIsError) ShowTooltip = false;
                    if (ObjectHover > 0)
                    {
                        var obj = vm.GetObjectById(ObjectHover);
                        if (!TipIsError && obj != null)
                        {
                            if (obj is VMAvatar)
                            {
                                if (((VMAvatar)obj).GetPersonData(VMPersonDataVariable.PersonType) != 255)
                                {
                                    state.UIState.TooltipProperties.Show = true;
                                    state.UIState.TooltipProperties.Color = Color.Black;
                                    state.UIState.TooltipProperties.Opacity = 1;
                                    state.UIState.TooltipProperties.Position = new Vector2(state.MouseState.X,
                                        state.MouseState.Y);
                                    state.UIState.Tooltip = GetAvatarString(obj as VMAvatar);
                                    state.UIState.TooltipProperties.UpdateDead = false;
                                    ShowTooltip = true;
                                }
                            }
                            else if (((VMGameObject)obj).Disabled > 0)
                            {
                                var flags = ((VMGameObject)obj).Disabled;
                                if ((flags & VMGameObjectDisableFlags.ForSale) > 0)
                                {
                                    //for sale
                                    //try to get catalog price
                                    var guid = obj.MasterDefinition?.GUID ?? obj.Object.OBJ.GUID;
                                    var item = Content.Content.Get().WorldCatalog.GetItemByGUID(guid);

                                    var retailPrice = (int?)(item?.Price) ?? obj.MultitileGroup.Price;
                                    var salePrice = obj.MultitileGroup.SalePrice;
                                    ShowErrorTooltip(state, 22, false, "$" + retailPrice.ToString("##,#0"), "$" + salePrice.ToString("##,#0"));
                                    TipIsError = false;
                                }
                            }

                        }
                    }
                    if (!ShowTooltip)
                    {
                        state.UIState.TooltipProperties.Show = false;
                        state.UIState.TooltipProperties.Opacity = 0;
                    }
                }
            }
            else
            {
                ObjectHover = 0;
            }

            if (!scrolled)
            { //set cursor depending on interaction availability
                CursorType cursor;

                if (PieMenu == null && MouseIsOn)
                {
                    if (ObjectHover == 0)
                    {
                        cursor = CursorType.LiveNothing;
                    }
                    else
                    {
                        if (InteractionsAvailable)
                        {
                            var obj = vm.GetObjectById(ObjectHover);
                            if (obj is VMAvatar)
                            {
                                cursor = (((VMAvatar)obj).GetPersonData(VMPersonDataVariable.PersonType) < 254) ? CursorType.LivePerson : CursorType.LiveObjectAvail;
                            }
                            else
                            {
                                var tsoState = obj?.PlatformState as VMTSOObjectState;
                                if (tsoState != null) cursor = CursorType.LiveObjectAvail + tsoState.UpgradeLevel;
                                else cursor = CursorType.LiveObjectAvail;
                            }
                        }
                        else
                        {
                            cursor = CursorType.LiveObjectUnavail;
                        }
                    }
                } else
                {

                    cursor = CursorType.Normal;
                } 

                CursorManager.INSTANCE.SetCursor(cursor);
            }

        }

        private string GetAvatarString(VMAvatar ava)
        {
            int prefixNum = 3;
            string prefixSrc = "217";
            var personType = ava.GetPersonData(VMPersonDataVariable.PersonType);

            if (personType >= 254) return ava.ToString();
            else if (ava.IsPet) prefixNum = 5;
            else if (ava.PersistID == 0) prefixNum = 4;
            else
            {
                var permissionsLevel = ((VMTSOAvatarState)ava.TSOState).Permissions;
                if (vm.TSOState.CommunityLot)
                {
                    switch (permissionsLevel)
                    {
                        case VMTSOAvatarPermissions.Visitor: prefixNum = 3; break;
                        case VMTSOAvatarPermissions.Roommate: prefixSrc = "f114"; prefixNum = 11; break;
                        case VMTSOAvatarPermissions.BuildBuyRoommate: prefixSrc = "f114"; prefixNum = 10; break;
                        case VMTSOAvatarPermissions.Admin: prefixSrc = "f114"; prefixNum = 17; break;
                        case VMTSOAvatarPermissions.Owner: prefixNum = 1; break;
                    }
                }
                else
                {
                    switch (permissionsLevel)
                    {
                        case VMTSOAvatarPermissions.Visitor: prefixNum = 3; break;
                        case VMTSOAvatarPermissions.Roommate:
                        case VMTSOAvatarPermissions.BuildBuyRoommate: prefixNum = 2; break;
                        case VMTSOAvatarPermissions.Admin: prefixSrc = "f114"; prefixNum = 17; break;
                        case VMTSOAvatarPermissions.Owner: prefixNum = 1; break;
                    }
                }
            }

            var result = GameFacade.Strings.GetString(prefixSrc, prefixNum.ToString()) + ava.ToString();
            if ((ava.AvatarState as VMTSOAvatarState).Flags.HasFlag(VMTSOAvatarFlags.Mayor))
            {
                result += GameFacade.Strings.GetString("f114", "8");
            }
            return result;
        }

        public void Landed()
        {
            //hints for landing
            var hints = FSOFacade.Hints;
            hints.TriggerHint("land");

            if (vm.MyUID == vm.TSOState.OwnerID)
            {
                hints.TriggerHint("land:owner");
                hints.TriggerHint("land:owner:" + ((LotCategory)vm.TSOState.PropertyCategory).ToString());
            }
            else if (vm.TSOState.Roommates.Contains(vm.MyUID))
            {
                if (vm.TSOState.PropertyCategory == (int)LotCategory.community)
                {
                    hints.TriggerHint("ui:donator");
                }
                else
                {
                    hints.TriggerHint("land:roomie");
                }
            }
            else if (vm.GetGlobalValue(11) > -1)
            {
                var split = vm.LotName.Substring(1, vm.LotName.Length - 2).Split(':');
                if (split.Length > 1 && split[0] == "job")
                {
                    hints.TriggerHint("land:job" + split[1]);
                }
            }
            else
            {
                hints.TriggerHint("land:" + ((LotCategory)vm.TSOState.PropertyCategory).ToString());
            }

            //todo: land special objects

            if (vm.TSOState.SkillMode > 1 && 
                !(vm.TSOState.PropertyCategory == (byte)LotCategory.welcome 
                && ((ActiveEntity?.TSOState as VMTSOAvatarState)?.Flags ?? 0).HasFlag(VMTSOAvatarFlags.NewPlayer)))
                hints.TriggerHint("land:skilldisabled");

            //cache all roommate names
            if (UIScreen.Current is CoreGameScreen)
                vm.TSOState.Names = new VMDataServiceNameCache(FSOFacade.Kernel.Get<Common.DataService.IClientDataService>());
            foreach (var roomie in vm.TSOState.Roommates)
            {
                vm.TSOState.Names.Precache(vm, roomie);
            }
            vm.TSOState.Names.Precache(vm, vm.TSOState.OwnerID);

            var objOwners = new HashSet<uint>();
            foreach (var ent in vm.Context.ObjectQueries.MultitileByPersist)
            {
                var owner = (ent.Value.BaseObject?.TSOState as VMTSOObjectState)?.OwnerID;
                if (owner != null) objOwners.Add(owner.Value);
            }
            foreach (var owner in objOwners)
                vm.TSOState.Names.Precache(vm, owner);

            HasLanded = true;
        }

        public void RefreshCut()
        {
            LastFloor = -1;
            LastWallMode = -1;

            if (vm.Context.Blueprint != null && LastCuts != null)
            {
                vm.Context.Blueprint.Cutaway = LastCuts;
                vm.Context.Blueprint.Changes.SetFlag(BlueprintGlobalChanges.WALL_CUT_CHANGED);
            }

            //MouseCutRect = new Rectangle(0,0,0,0);
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (RMBScroll)
            {
                DrawLocalTexture(batch, RMBCursor, new Vector2(RMBScrollX - RMBCursor.Width/2, RMBScrollY - RMBCursor.Height / 2));
            }
            base.Draw(batch);
        }

        public void SetTargetZoom(WorldZoom zoom)
        {
            switch (zoom)
            {
                case WorldZoom.Near:
                    TargetZoom = 1f; break;
                case WorldZoom.Medium:
                    TargetZoom = 0.5f; break;
                case WorldZoom.Far:
                    TargetZoom = 0.25f; break;
            }
            LastZoom = World.State.Zoom;
        }

        private WorldZoom LastZoom;
        public override void Update(UpdateState state)
        {
            base.Update(state);

            if (!vm.Ready || vm.Context.Architecture == null) return;

            //handling smooth scaled zoom
            var camType = World.State.Cameras.ActiveType;
            Touch._3D = camType != LotView.Utils.Camera.CameraControllerType._2D;
            if (World.State.Cameras.ActiveType == LotView.Utils.Camera.CameraControllerType._3D)
            {
                if (World.BackbufferScale != 1) World.BackbufferScale = 1;
                var s3d = World.State.Cameras.Camera3D; //((WorldStateRC)World.State);
                if (TargetZoom < -0.65f)
                {
                    //switch to city
                    (UIScreen.Current as Screens.CoreGameScreen)?.ZoomToCity();
                    TargetZoom = 0;
                    //TargetZoom -= (TargetZoom - 0.25f) * (1f - (float)Math.Pow(0.975f, 60f / FSOEnvironment.RefreshRate));
                }
                else if (TargetZoom < -0.25f)
                {
                    TargetZoom -= (TargetZoom - 0.25f) * (1f - (float)Math.Pow(0.975f, 60f / FSOEnvironment.RefreshRate));
                }
                s3d.Zoom3D += ((9.75f - (TargetZoom - 0.25f) * 5.7f) - s3d.Zoom3D) / 10;

            }
            else if (World.State.Cameras.ActiveType == LotView.Utils.Camera.CameraControllerType._2D)
            {
                if (World.State.Zoom != LastZoom)
                {
                    //zoom has been changed by something else. inherit the value
                    SetTargetZoom(World.State.Zoom);
                    LastZoom = World.State.Zoom;
                }

                float BaseScale;
                WorldZoom targetZoom;
                if (TargetZoom < 0.5f)
                {
                    targetZoom = WorldZoom.Far;
                    BaseScale = 0.25f;
                }
                else if (TargetZoom < 1f)
                {
                    targetZoom = WorldZoom.Medium;
                    BaseScale = 0.5f;
                }
                else
                {
                    targetZoom = WorldZoom.Near;
                    BaseScale = 1f;
                }
                World.BackbufferScale = TargetZoom / BaseScale;
                if (World.State.Zoom != targetZoom) World.State.Zoom = targetZoom;
                LastZoom = targetZoom;
                WorldConfig.Current.SmoothZoom = false;
            }

            Cheats.Update(state);
            AvatarDS.Update();
            if (ActiveEntity == null || ActiveEntity.Dead || ActiveEntity.PersistID != SelectedSimID)
            {
                ActiveEntity = vm.Entities.FirstOrDefault(x => x is VMAvatar && x.PersistID == SelectedSimID); //try and hook onto a sim if we have none selected.
                if (ActiveEntity == null) ActiveEntity = vm.Entities.FirstOrDefault(x => x is VMAvatar && x.PersistID > 0);
                else if (!HasInitUserProps)
                {
                    InitUserProps();
                }

                if (!FoundMe && ActiveEntity != null)
                {
                    vm.Context.World.State.CenterTile = new Vector2(ActiveEntity.VisualPosition.X, ActiveEntity.VisualPosition.Y);
                    vm.Context.World.State.ScrollAnchor = null;
                    FoundMe = true;
                }
                Queue.QueueOwner = ActiveEntity;
            }

            if (GotoObject == null) GotoObject = vm.Context.CreateObjectInstance(GOTO_GUID, LotTilePos.OUT_OF_WORLD, Direction.NORTH, true).Objects[0];

            if (ActiveEntity != null && BlockingDialog != null)
            {
                //are we still waiting on a blocking dialog? if not, cancel.
                if (ActiveEntity.Thread != null && (ActiveEntity.Thread.BlockingState == null || !(ActiveEntity.Thread.BlockingState is VMDialogResult)))
                {
                    UIScreen.RemoveDialog(BlockingDialog);
                    LastDialogID = 0;
                    BlockingDialog = null;
                }
            }

            if (DialogTakeFocus != null)
            {
                // Right now just steal it from the game. In future it should be given to the OK button.
                state.InputManager.SetFocus(this);
                DialogTakeFocus = null;
            }

            if (Visible)
            {
                if (!HasLanded) Landed();
                UpdateChatTitle();
                if (ShowTooltip) state.UIState.TooltipProperties.UpdateDead = false;

                var nofocus = state.InputManager.GetFocus() == null;

                bool scrolled = false;
                float firstPersonTuning = vm?.Tuning?.GetTuning("aprilfools", 0, 2023) ?? 0;
                FirstPerson = firstPersonTuning != 0 && World.State.Cameras.ActiveType == LotView.Utils.Camera.CameraControllerType.FirstPerson;

                if (firstPersonTuning == 2 && !FirstPerson)
                {
                    if (!FSOEnvironment.Enable3D)
                    {
                        vm.SendCommand(new VMNetDirectControlToggleCommand
                        {
                            Enable = false
                        });
                    }
                    else
                    {
                        World.State.SetCameraType(World, LotView.Utils.Camera.CameraControllerType.FirstPerson, 0);
                        FirstPerson = true;
                    }
                }

                if (FirstPerson != LastFirstPerson)
                {
                    nofocus = true;
                    World.CanSwitchCameras = firstPersonTuning != 2;
                    World.State.DrawRoofs = FirstPerson;
                    WallsMode = FirstPerson ? 3 : 1;
                    vm.SendCommand(new VMNetDirectControlToggleCommand
                    {
                        Enable = FirstPerson
                    });

                    LastFirstPerson = FirstPerson;
                }

                bool firstPersonControls = FirstPerson && state.WindowFocused && nofocus && PieMenu == null;

                World.State.Cameras.CameraFirstPerson.CaptureMouse = FirstPerson ? firstPersonControls : true;

                if (KBScroll || firstPersonControls)
                {
                    int KeyboardAxisX = 0;
                    int KeyboardAxisY = 0;
                    Vector2 scrollBy = new Vector2();
                    if (state.KeyboardState.IsKeyDown(Keys.Up) || state.KeyboardState.IsKeyDown(Keys.W)) KeyboardAxisY -= 1;
                    if (state.KeyboardState.IsKeyDown(Keys.Left) || state.KeyboardState.IsKeyDown(Keys.A)) KeyboardAxisX -= 1;
                    if (state.KeyboardState.IsKeyDown(Keys.Down) || state.KeyboardState.IsKeyDown(Keys.S)) KeyboardAxisY += 1;
                    if (state.KeyboardState.IsKeyDown(Keys.Right) || state.KeyboardState.IsKeyDown(Keys.D)) KeyboardAxisX += 1;
                    scrollBy = new Vector2(KeyboardAxisX, KeyboardAxisY);

                    if (FirstPerson && ActiveEntity != null)
                    {
                        if (state.KeyboardState.IsKeyDown(Keys.Escape)) state.InputManager.SetFocus(this);
                        if (state.KeyboardState.IsKeyDown(Keys.Back))
                        {
                            if (ActiveEntity.Thread.ActiveQueueBlock != -1)
                            {
                                var action = ActiveEntity.Thread.Queue[ActiveEntity.Thread.ActiveQueueBlock];
                                if (action.Mode == VMQueueMode.Normal || action.Mode == VMQueueMode.ParentIdle)
                                {
                                    HIT.HITVM.Get().PlaySoundEvent(UISounds.QueueDelete);
                                    vm.SendCommand(new VMNetInteractionCancelCmd() { ActionUID = action.UID });
                                }
                            }
                        } 

                        World.State.ScrollAnchor = ActiveEntity.WorldUI as AvatarComponent;

                        var facingVec = World.Transform(new Vector2(0, -1));

                        int intensity = 0;
                        short direction = 0;

                        if (scrollBy != Vector2.Zero)
                        {
                            var moveVec = World.Transform(scrollBy);
                            moveVec.Normalize();

                            direction = (short)((Math.Atan2(moveVec.X, -moveVec.Y) / Math.PI) * 0x7FFF);
                            intensity = 100;
                        }

                        short faceDir = (short)((Math.Atan2(facingVec.X, -facingVec.Y) / Math.PI) * 0x7FFF);
                        bool sprint = state.KeyboardState.IsKeyDown(Keys.LeftShift) || state.KeyboardState.IsKeyDown(Keys.RightShift);

                        var lastStack = ActiveEntity.Thread.Stack.LastOrDefault();

                        var dir = World.Camera.Target - World.Camera.Position;

                        FirstPersonSinceUpdate += 1f / FSOEnvironment.RefreshRate;

                        if (lastStack is VMDirectControlFrame frame)
                        {
                            frame.SendUserControls(new VMDirectControlInput()
                            {
                                ID = FirstPersonID++,
                                Direction = direction,
                                InputIntensity = intensity,
                                LookDirectionInt = faceDir,
                                LookDirectionReal = dir,
                                Sprint = sprint
                            });
                        }
                        else
                        {
                            if (FirstPersonSinceUpdate > 1/30f)
                            {
                                vm.SendCommand(new VMNetDirectControlCommand()
                                {
                                    Partial = true,
                                    Input = new VMDirectControlInput()
                                    {
                                        LookDirectionReal = dir
                                    }
                                });

                                FirstPersonSinceUpdate = FirstPersonSinceUpdate % (1 / 30f);
                            }
                        }
                    }
                    else
                    {
                        World.State.ScrollAnchor = null;

                        var currentFpAva = World.State.Cameras.CameraFirstPerson.FirstPersonAvatar;
                        if (currentFpAva != null)
                        {
                            currentFpAva.Avatar.HideHead = false;
                            World.State.Cameras.CameraFirstPerson.FirstPersonAvatar = null;
                        }

                        scrollBy *= 0.05f;

                        World.Scroll(scrollBy * (60f / FSOEnvironment.RefreshRate));
                    }
                }
                if (RMBScroll)
                {
                    World.State.ScrollAnchor = null;
                    Vector2 scrollBy = new Vector2();
                    if (state.TouchMode)
                    {
                        scrollBy = new Vector2(RMBScrollX - state.MouseState.X, RMBScrollY - state.MouseState.Y);
                        RMBScrollX = state.MouseState.X;
                        RMBScrollY = state.MouseState.Y;
                        scrollBy /= 128f;
                        scrollBy /= FSOEnvironment.DPIScaleFactor;
                    } else
                    {
                        scrollBy = new Vector2(state.MouseState.X - RMBScrollX, state.MouseState.Y - RMBScrollY);
                        scrollBy *= 0.0005f;

                        var angle = (Math.Atan2(state.MouseState.X - RMBScrollX, (RMBScrollY - state.MouseState.Y)*2) / Math.PI) * 4;
                        angle += 8;
                        angle %= 8;

                        CursorType type = CursorType.ArrowUp;
                        switch ((int)Math.Round(angle))
                        {
                            case 0: type = CursorType.ArrowUp; break;
                            case 1: type = CursorType.ArrowUpRight; break;
                            case 2: type = CursorType.ArrowRight; break;
                            case 3: type = CursorType.ArrowDownRight; break;
                            case 4: type = CursorType.ArrowDown; break;
                            case 5: type = CursorType.ArrowDownLeft; break;
                            case 6: type = CursorType.ArrowLeft; break;
                            case 7: type = CursorType.ArrowUpLeft; break;
                        }
                        GameFacade.Cursor.SetCursor(type);
                    }
                    World.Scroll(scrollBy * (60f / FSOEnvironment.RefreshRate));
                    scrolled = true;
                }
                var keyst = state.KeyboardState;
                KBScroll = state.WindowFocused && nofocus &&
                    (keyst.IsKeyDown(Keys.Up) || keyst.IsKeyDown(Keys.Left) || keyst.IsKeyDown(Keys.Down) || keyst.IsKeyDown(Keys.Right) ||
                    keyst.IsKeyDown(Keys.W) || keyst.IsKeyDown(Keys.A) || keyst.IsKeyDown(Keys.S) || keyst.IsKeyDown(Keys.D));
                if (MouseIsOn)
                {
                    if (state.MouseState.RightButton == ButtonState.Pressed)
                    {
                        if (!RMBScroll)
                        {
                            RMBScroll = true;
                            state.InputManager.SetFocus(null);
                            RMBScrollX = state.MouseState.X;
                            RMBScrollY = state.MouseState.Y;
                        }
                    }
                    else
                    {
                        if (!scrolled && GlobalSettings.Default.EdgeScroll && !state.TouchMode) scrolled = World.TestScroll(state);
                    }
                }

                if (state.MouseState.RightButton != ButtonState.Pressed)
                {
                    if (RMBScroll) GameFacade.Cursor.SetCursor(CursorType.Normal);
                    RMBScroll = false;
                }

                if (LiveMode) LiveModeUpdate(state, scrolled);
                else if (CustomControl != null)
                {
                    CustomControl.Update(state, scrolled);
                }
                else ObjectHolder.Update(state, scrolled);

                //set cutaway around mouse
                UpdateCutaway(state);
                if(state.WindowFocused && nofocus)
                {
                    if (state.NewKeys.Contains(Keys.S) && state.KeyboardState.IsKeyDown(Keys.LeftControl) && !state.KeyboardState.IsKeyDown(Keys.RightAlt))
                    {
                        //save lot
                        if (LotSaveDialog == null) SaveLot();
                    }
                    else if (state.NewKeys.Contains(Keys.F) && state.KeyboardState.IsKeyDown(Keys.LeftControl) && !state.KeyboardState.IsKeyDown(Keys.RightAlt))
                    {
                        //save facade
                        if (LotSaveDialog == null) SaveFacade(state.KeyboardState.IsKeyDown(Keys.LeftAlt));
                    }
                }
            }
        }

        private void InitUserProps()
        {
            if (GlobalSettings.Default.ChatColor == 0)
            {
                var rand = new Random();
                GlobalSettings.Default.ChatColor = VMTSOAvatarState.RandomColours[rand.Next(VMTSOAvatarState.RandomColours.Length)].PackedValue;
                GlobalSettings.Default.Save();
            }
            vm.SendCommand(new VMNetChatParamCmd()
            {
                Col = new Color(GlobalSettings.Default.ChatColor),
                Pitch = (sbyte)GlobalSettings.Default.ChatTTSPitch
            });

            //init tuning vars for UI
            var emojiOnly = (int)(vm.Tuning.GetTuning("ui", 0, 0) ?? 0);
            if (emojiOnly != GlobalSettings.Default.ChatOnlyEmoji)
            {
                GlobalSettings.Default.ChatOnlyEmoji = emojiOnly;
                GlobalSettings.Default.Save();
            }

            HasInitUserProps = true;
        }

        private void SaveLot()
        {
            LotSaveDialog = new UIAlert(new UIAlertOptions
            {
                Title = "Save Lot",
                Message = "Enter a filename to save this lot with. It will save locally to Content/LocalHouse, where it can be used in sandbox mode.",
                TextEntry = true,
                Width = 500,
                Buttons = new UIAlertButton[]
                {
                    new UIAlertButton(UIAlertButtonType.Cancel, (b) => { UIScreen.RemoveDialog(LotSaveDialog); LotSaveDialog = null; }),
                    new UIAlertButton(UIAlertButtonType.OK, (b) =>
                    {
                        try {
                            var exporter = new VMWorldExporter();
                            exporter.SaveHouse(vm, Path.Combine(FSOEnvironment.UserDir, ("Blueprints/"+LotSaveDialog.ResponseText+".xml")));
                            var marshal = vm.Save();
                            Directory.CreateDirectory(Path.Combine(FSOEnvironment.UserDir, "LocalHouse/"));
                            using (var output = new FileStream(Path.Combine(FSOEnvironment.UserDir, "LocalHouse/"+LotSaveDialog.ResponseText+".fsov"), FileMode.Create))
                            {
                                marshal.SerializeInto(new BinaryWriter(output));
                            }
                            if (vm.GlobalLink != null) ((VMTSOGlobalLinkStub)vm.GlobalLink).Database.Save();

                            UIScreen.GlobalShowAlert(new UIAlertOptions { Message = "Save successful!" }, true);
                        } catch
                        {
                            UIScreen.GlobalShowAlert(new UIAlertOptions { Message = "Lot failed to save. You may need to run the game as administrator." }, true);
                        }
                        UIScreen.RemoveDialog(LotSaveDialog); LotSaveDialog = null;
                    })
                }
            });
            LotSaveDialog.ResponseText = "house_00";
            UIScreen.GlobalShowDialog(LotSaveDialog, true);
        }

        private void SaveFacade(bool toObject)
        {
            LotSaveDialog = new UIAlert(new UIAlertOptions
            {
                Title = "Save Lot Facade",
                Message = "Enter a filename to save this lotfacade with. It will save locally to Content/Facades/<name>/ as an obj file, for use testing facades or elsewhere.",
                TextEntry = true,
                Width = 500,
                Buttons = new UIAlertButton[]
                {
                    new UIAlertButton(UIAlertButtonType.Cancel, (b) => { UIScreen.RemoveDialog(LotSaveDialog); LotSaveDialog = null; }),
                    new UIAlertButton(UIAlertButtonType.OK, (b) =>
                    {
                        try {
                            for (int i=0; i<(toObject?2:1); i++) {
                                var path = Path.Combine(FSOEnvironment.UserDir, "Facades/"+LotSaveDialog.ResponseText+"/");
                                Directory.CreateDirectory(path);

                                //turn all lights on
                                if (i == 1) {
                                    foreach (var light in vm.Entities.Where(x => x.Object.Resource.SemiGlobal?.Iff?.Filename == "lightglobals.iff"))
                                    {
                                        light.SetValue(SimAntics.Model.VMStackObjectVariable.LightingContribution, 100);
                                    }
                                    vm.Context.Architecture.SignalAllDirty();
                                    vm.Context.Architecture.Tick();
                                }
                                SetOutsideTime(GameFacade.GraphicsDevice, vm, World, (1-i)*0.5f, false);

                                var facade = new LotFacadeGenerator();
                                if (toObject)
                                {
                                    LotFacadeGenerator.WALL_HEIGHT = 6*3;
                                    LotFacadeGenerator.WALL_WIDTH = 6;
                                    var numInd = LotSaveDialog.ResponseText.ToList().FindIndex(x => char.IsDigit(x));
                                    facade.TexBase = int.Parse(LotSaveDialog.ResponseText.Substring(numInd)) * 4 + i*2;
                                    facade.RoofOnFloor = true;
                                }
                                facade.RoofOnFloor = true;
                                facade.Generate(GameFacade.GraphicsDevice, World, vm.Context.Blueprint);
                                facade.SaveToPath(path);
                                UIScreen.GlobalShowAlert(new UIAlertOptions { Message = "Save successful!" }, true);
                            }
                        } catch
                        {
                            UIScreen.GlobalShowAlert(new UIAlertOptions { Message = "Lot failed to save. You may need to run the game as administrator." }, true);
                        }
                        UIScreen.RemoveDialog(LotSaveDialog); LotSaveDialog = null;
                    })
                }
            });
            LotSaveDialog.ResponseText = vm.LotName;
            UIScreen.GlobalShowDialog(LotSaveDialog, true);
        }

        private static void SetOutsideTime(GraphicsDevice gd, VM vm, World world, float time, bool lightsOn)
        {
            vm.Context.Architecture.SetTimeOfDay(time);
            world.Force2DPredraw(gd);
            vm.Context.Architecture.SetTimeOfDay();
        }

        private void UpdateCutaway(UpdateState state)
        {
            if (vm.Context.Blueprint != null)
            {
                World.State.DynamicCutaway = (WallsMode == 1);
                //first we need to cycle the rooms that are being cutaway. Keep this up even if we're in all-cut mode.
                var mouseTilePos = World.EstTileAtPosWithScroll(GetScaledPoint(state.MouseState.Position).ToVector2());
                var roomHover = vm.Context.GetRoomAt(LotTilePos.FromBigTile((short)(mouseTilePos.X), (short)(mouseTilePos.Y), World.State.Level));
                var outside = (vm.Context.RoomInfo[roomHover].Room.IsOutside);
                if (!outside && !CutRooms.Contains(roomHover))
                    CutRooms.Add(roomHover); //outside hover should not persist like with other rooms.
                while (CutRooms.Count > 3) CutRooms.Remove(CutRooms.ElementAt(0));

                if (LastWallMode != WallsMode)
                {
                    if (WallsMode == 0) //walls down
                    {
                        LastCuts = new bool[vm.Context.Architecture.Width * vm.Context.Architecture.Height];
                        vm.Context.Blueprint.Cutaway = LastCuts;
                        vm.Context.Blueprint.Changes.SetFlag(BlueprintGlobalChanges.WALL_CUT_CHANGED);
                        for (int i = 0; i < LastCuts.Length; i++) LastCuts[i] = true;
                    }
                    else if (WallsMode == 1)
                    {
                        MouseCutRect = new Rectangle();
                        LastCutRooms = new HashSet<uint>() { uint.MaxValue }; //must regenerate cuts
                    }
                    else //walls up or roof
                    {
                        LastCuts = new bool[vm.Context.Architecture.Width * vm.Context.Architecture.Height];
                        vm.Context.Blueprint.Cutaway = LastCuts;
                        vm.Context.Blueprint.Changes.SetFlag(BlueprintGlobalChanges.WALL_CUT_CHANGED);
                    }
                    LastWallMode = WallsMode;
                }

                if (WallsMode == 1)
                {
                    if (RMBScroll || !MouseIsOn) return;
                    int recut = 0;
                    var finalRooms = new HashSet<uint>(CutRooms);

                    var newCut = new Rectangle((int)(mouseTilePos.X - 2.5), (int)(mouseTilePos.Y - 2.5), 5, 5);
                    newCut.X -= VMArchitectureTools.CutCheckDir[(int)World.State.CutRotation][0] * 2;
                    newCut.Y -= VMArchitectureTools.CutCheckDir[(int)World.State.CutRotation][1] * 2;
                    if (newCut != MouseCutRect)
                    {
                        MouseCutRect = newCut;
                        recut = 1;
                    }

                    if (LastFloor != World.State.Level || LastRotation != World.State.CutRotation || !finalRooms.SetEquals(LastCutRooms))
                    {
                        LastCuts = VMArchitectureTools.GenerateRoomCut(vm.Context.Architecture, World.State.Level, World.State.CutRotation, finalRooms);
                        recut = 2;
                        LastFloor = World.State.Level;
                        LastRotation = World.State.CutRotation;
                    }
                    LastCutRooms = finalRooms;

                    if (recut > 0)
                    {
                        var finalCut = new bool[LastCuts.Length];
                        Array.Copy(LastCuts, finalCut, LastCuts.Length);
                        var notableChange = VMArchitectureTools.ApplyCutRectangle(vm.Context.Architecture, World.State.Level, finalCut, MouseCutRect);
                        if (recut > 1 || notableChange || LastRectCutNotable)
                        {
                            vm.Context.Blueprint.Cutaway = finalCut;
                            vm.Context.Blueprint.Changes.SetFlag(BlueprintGlobalChanges.WALL_CUT_CHANGED);
                        }
                        LastRectCutNotable = notableChange;
                    }
                }
            }
        }

        public void Dispose()
        {
            AvatarDS.ReleaseAvatars();
        }

        public void ClearCenter()
        {
        }

        public void OnFocusChanged(FocusEvent newFocus)
        {

        }
    }
}
