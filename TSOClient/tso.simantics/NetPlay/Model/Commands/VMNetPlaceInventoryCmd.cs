﻿using FSO.LotView.Model;
using FSO.SimAntics.Entities;
using FSO.SimAntics.Marshals;
using FSO.SimAntics.Model;
using FSO.SimAntics.Model.Platform;
using FSO.SimAntics.Model.TSOPlatform;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FSO.SimAntics.NetPlay.Model.Commands
{
    public class VMNetPlaceInventoryCmd : VMNetCommandBodyAbstract
    {
        //request from client
        public uint ObjectPID;
        public short x;
        public short y;
        public sbyte level;
        public Direction dir;

        public PurchaseMode Mode = PurchaseMode.Normal;

        //data sent back to the client only
        public VMInventoryRestoreObject Info = new VMInventoryRestoreObject();

        //internal
        private VMMultitileGroup CreatedGroup;
        public short CreatedBaseID => CreatedGroup?.BaseObject?.ObjectID ?? 0;
        public bool Verified;

        /// <summary>
        /// Sent to the client after the object data is retrieved by the client. If data is 0 length, intantiate a new object of the type.
        /// Can rarely cause the object to be sent BACK to inventory if it cannot be placed in the intended location.
        /// </summary>
        /// <param name="vm"></param>
        /// <param name="caller"></param>
        /// <returns></returns>
        public override bool Execute(VM vm, VMAvatar caller)
        {
            //careful here! if the object can't be placed, we have to put their object back
            if (!vm.Context.ObjectQueries.MultitileByPersist.ContainsKey(Info.GUID) && TryPlace(vm, caller))
            {
                if (CreatedGroup.BaseObject is VMGameObject)
                {
                    foreach (var o in CreatedGroup.Objects) ((VMGameObject)o).Disabled &= ~VMGameObjectDisableFlags.ForSale;
                }
                CreatedGroup.SalePrice = -1;
                return true;
            }
            else
            {
                //oops, we can't place this object or some other issue occured. move it back to inventory.
                if (CreatedGroup != null)
                {
                    vm.Context.ObjectQueries.RemoveMultitilePersist(vm, ObjectPID);
                    foreach (var o in CreatedGroup.Objects) o.PersistID = 0; //no longer representative of the object in db.
                    CreatedGroup.Delete(vm.Context);
                }
                if (vm.GlobalLink != null)
                {
                    //do a force move to inventory here. do not need to resave state, since it has not changed since the place.
                    //if the server crashes between setting the object on this lot and now, the
                    //server should detect that an object says it is "on" this lot, but it really isn't physically
                    //and move it back to inventory without a state save.
                    //
                    //owned objects that are "out of world" should be moved to inventory on load. (FULL move, for safety)
                    vm.GlobalLink.ForceInInventory(vm, ObjectPID, (bool success, uint pid) => {}); 
                }
            }
            return false;
        }

        private bool TryPlace(VM vm, VMAvatar caller)
        {
            var internalMode = caller == null;
            if (Mode != PurchaseMode.Donate && !vm.PlatformState.CanPlaceNewUserObject(vm)) return false;
            if (Mode == PurchaseMode.Donate && !vm.PlatformState.CanPlaceNewDonatedObject(vm)) return false;

            VMStandaloneObjectMarshal state;

            var catalog = Content.Content.Get().WorldCatalog;
            var item = catalog.GetItemByGUID(Info.GUID);

            if (caller != null && (item?.DisableLevel ?? 0) > 2)
            {
                //object cannot be placed (disable level 3)
                return false;
            }

            if ((Info.Data?.Length ?? 0) == 0) state = null;
            else
            {
                state = new VMStandaloneObjectMarshal();
                try
                {
                    using (var reader = new BinaryReader(new MemoryStream(Info.Data)))
                    {
                        state.Deserialize(reader);
                    }
                    foreach (var e in state.Entities)
                    {
                        // Undisable the object, and remove any non-persist relationships.
                        ((VMGameObjectMarshal)e).Disabled = 0;
                        e.MeToObject = new VMEntityRelationshipMarshal[0];
                    }
                }
                catch (Exception)
                {
                    //failed to restore state
                    state = null;
                }
            }
            
            if (state != null)
            {
                CreatedGroup = state.CreateInstance(vm, false);
                CreatedGroup.ChangePosition(new LotTilePos(x, y, level), dir, vm.Context, VMPlaceRequestFlags.UserPlacement);

                CreatedGroup.ExecuteEntryPoint(11, vm.Context); //User Placement
                if (CreatedGroup.Objects.Count == 0) return false;
                if (CreatedGroup.BaseObject.Position == LotTilePos.OUT_OF_WORLD && !internalMode)
                {
                    return false;
                }
            }
            else
            {
                CreatedGroup = vm.Context.CreateObjectInstance(Info.GUID, LotTilePos.OUT_OF_WORLD, dir);
                if (CreatedGroup == null) return false;
                CreatedGroup.ChangePosition(new LotTilePos(x, y, level), dir, vm.Context, VMPlaceRequestFlags.UserPlacement);

                CreatedGroup.ExecuteEntryPoint(11, vm.Context); //User Placement
                if (CreatedGroup.Objects.Count == 0) return false;

                if (CreatedGroup.BaseObject.Position == LotTilePos.OUT_OF_WORLD && !internalMode)
                {
                    return false;
                }
            }

            foreach (var obj in CreatedGroup.Objects)
            {
                var tsostate = (obj.PlatformState as VMTSOObjectState);
                if (tsostate != null) {
                    if (caller != null) tsostate.OwnerID = caller.PersistID;
                    bool reinitRequired = false;
                    if (Info.UpgradeLevel > tsostate.UpgradeLevel)
                    {
                        tsostate.UpgradeLevel = Info.UpgradeLevel;
                        reinitRequired = true;
                    }
                    obj.UpdateTuning(vm);
                    if (reinitRequired) VMNetUpgradeCmd.TryReinit(obj, vm, tsostate.UpgradeLevel);
                }
                obj.PersistID = ObjectPID;
                ((VMGameObject)obj).DisableIfTSOCategoryWrong(vm.Context);
            }
            vm.Context.ObjectQueries.RegisterMultitilePersist(CreatedGroup, ObjectPID);

            //is this my sim's object? try remove it from our local inventory representaton
            if (((VMTSOObjectState)CreatedGroup.BaseObject.TSOState).OwnerID == vm.MyUID && Info.RestoreType != VMInventoryRestoreType.CopyOOW)
            {
                var index = vm.MyInventory.FindIndex(x => x.ObjectPID == ObjectPID);
                if (index != -1) vm.MyInventory.RemoveAt(index);
            }

            if (Mode == PurchaseMode.Donate)
            {
                //this object should be donated.
                (CreatedGroup.BaseObject.TSOState as VMTSOObjectState).Donate(vm, CreatedGroup.BaseObject);
            }

            if (caller != null)
            {
                vm.SignalChatEvent(new VMChatEvent(caller, VMChatEventType.Arch,
                    caller.Name,
                    vm.GetUserIP(caller.PersistID),
                    "placed (from inventory) " + CreatedGroup.BaseObject.ToString() + " at (" + x / 16f + ", " + y / 16f + ", " + level + ")"
                ));
            }
            return true;
        }

        /// <summary>
        /// Called on server when someone requests to place an object from inventory. 
        /// Immediately "fails" verification to be queued later, once the data has been retrieved.
        /// </summary>
        /// <param name="vm"></param>
        /// <param name="caller"></param>
        /// <returns></returns>
        public override bool Verify(VM vm, VMAvatar caller)
        {
            if (Verified) return true; //set internally when transaction succeeds. trust that the verification happened.
            //typically null caller, non-roommate cause failure. some lot specific things may apply.
            Mode = vm.PlatformState.Validator.GetPurchaseMode(Mode, caller, 0, true);
            if (Mode == PurchaseMode.Disallowed ||
                !vm.TSOState.CanPlaceNewUserObject(vm))
                return false;

            vm.GlobalLink.RetrieveFromInventory(vm, ObjectPID, caller.PersistID, true, (info) =>
            {
                if (info.GUID == 0) return; //todo: error feedback?
                Info = info;
                Verified = true;
                vm.ForwardCommand(this);
            });

            return false;
        }

        #region VMSerializable Members

        public override void SerializeInto(BinaryWriter writer)
        {
            base.SerializeInto(writer);
            writer.Write(ObjectPID);
            writer.Write(x);
            writer.Write(y);
            writer.Write(level);
            writer.Write((byte)dir);

            Info.SerializeInto(writer);

            writer.Write((byte)Mode);
        }

        public override void Deserialize(BinaryReader reader)
        {
            base.Deserialize(reader);
            ObjectPID = reader.ReadUInt32();
            x = reader.ReadInt16();
            y = reader.ReadInt16();
            level = reader.ReadSByte();
            dir = (Direction)reader.ReadByte();

            Info.Deserialize(reader);

            Mode = (PurchaseMode)reader.ReadByte();
        }

        #endregion
    }

    public class VMInventoryRestoreObject
    {
        public uint PersistID = 0;
        public uint GUID;
        public VMInventoryRestoreType RestoreType = VMInventoryRestoreType.Normal;

        //important state that is saved in the db for redundancy, in case inventory state is missing, outdated or corrupt
        //only used if save state is missing
        public byte UpgradeLevel; //prefer larger of restore data and this
        public int Wear; //prefer larger of restore data and this
        public List<short> Attributes = new List<short>();

        public VMInventoryRestoreObject()
        {

        }

        public VMInventoryRestoreObject(uint guid, byte upgradeLevel, int wear)
        {
            GUID = guid;
            UpgradeLevel = upgradeLevel;
            Wear = wear;
        }

        public byte[] Data;

        public void SerializeInto(BinaryWriter writer)
        {
            writer.Write(PersistID);
            writer.Write(GUID);
            writer.Write((byte)RestoreType);
            writer.Write(UpgradeLevel);
            writer.Write(Wear);
            writer.Write(Data?.Length ?? 0);
            if (Data != null) writer.Write(Data);
            writer.Write((byte)Attributes.Count);
            foreach (var attribute in Attributes) writer.Write(attribute);
        }

        public void Deserialize(BinaryReader reader)
        {
            PersistID = reader.ReadUInt32();
            GUID = reader.ReadUInt32();
            RestoreType = (VMInventoryRestoreType)reader.ReadByte();
            UpgradeLevel = reader.ReadByte();
            Wear = reader.ReadInt32();
            var length = reader.ReadInt32();
            if (length > 4096) throw new Exception("Object data cannot be this large!");
            Data = reader.ReadBytes(length);
            var attrCount = reader.ReadByte();
            Attributes.Clear();
            for (int i = 0; i < attrCount; i++) {
                Attributes.Add(reader.ReadInt16());
            }
        }
    }

    public enum VMInventoryRestoreType : byte
    {
        Normal,
        CreateOOW,
        CopyOOW
    }
}
