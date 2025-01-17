﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game.Entities;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using Torch.Commands;
using Torch.Commands.Permissions;
using Torch.Utils;
using Torch.API.Managers;
using VRage.Collections;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Game;
using VRage.Network;
using VRage.Replication;
using IMyDestroyableObject = VRage.Game.ModAPI.Interfaces.IMyDestroyableObject;



namespace Essentials
{
    [Category("entities")]
    public class EntityModule : CommandModule
    {
#pragma warning disable 649
        [ReflectedGetter(Name = "m_clientStates")]
        private static Func<MyReplicationServer, IDictionary> _clientStates;

        private const string CLIENT_DATA_TYPE_NAME = "VRage.Network.MyClient, VRage";
        [ReflectedGetter(TypeName = CLIENT_DATA_TYPE_NAME, Name = "Replicables")]
        private static Func<object, MyConcurrentDictionary<IMyReplicable, MyReplicableClientData>> _replicables;

        [ReflectedMethod(Name = "RemoveForClient", OverrideTypeNames = new[] { null, CLIENT_DATA_TYPE_NAME, null })]
        private static Action<MyReplicationServer, IMyReplicable, object, bool> _removeForClient;

        [ReflectedMethod(Name = "ForceReplicable")]
        private static Action<MyReplicationServer, IMyReplicable, Endpoint> _forceReplicable;
#pragma warning restore 649

        private static Dictionary<ulong, DateTime> _commandtimeout = new Dictionary<ulong, DateTime>();

        [Command("refresh", "Resyncs all entities for the player running the command.")]
        [Permission(MyPromoteLevel.None)]
        public void Refresh()
        {
            if (Context.Player == null)
                return;

            var steamid = Context.Player.SteamUserId;
            if (_commandtimeout.TryGetValue(steamid, out DateTime lastcommand))
            {
                TimeSpan difference = DateTime.Now - lastcommand;
                if (difference.TotalMinutes < 1)
                {
                    Context.Respond($"Cooldown active. You can use this command again in {difference.TotalSeconds:N0} seconds");
                    return;
                }
                else
                {
                    _commandtimeout[steamid] = DateTime.Now;
                }
            }
            else
            {
                _commandtimeout.Add(steamid, DateTime.Now);
            }

            var playerEndpoint = new Endpoint(Context.Player.SteamUserId, 0);
            var replicationServer = (MyReplicationServer)MyMultiplayer.ReplicationLayer;
            var clientDataDict = _clientStates.Invoke(replicationServer);
            object clientData;
            try
            {
                clientData = clientDataDict[playerEndpoint];
            }
            catch
            {
                return;
            }

            var clientReplicables = _replicables.Invoke(clientData);

            var replicableList = new List<IMyReplicable>(clientReplicables.Count);
            foreach (var pair in clientReplicables)
                replicableList.Add(pair.Key);

            foreach (var replicable in replicableList)
            {
                _removeForClient.Invoke(replicationServer, replicable, clientData, true);
                _forceReplicable.Invoke(replicationServer, replicable, playerEndpoint);
            }

            Context.Respond($"Forced replication of {replicableList.Count} entities.");
        }

        [Command("stop", "Stops an entity from moving")]
        [Permission(MyPromoteLevel.SpaceMaster)]
        public void Stop(string entityName)
        {
            if (!Utilities.TryGetEntityByNameOrId(entityName, out IMyEntity entity))
            {
                Context.Respond($"Entity '{entityName}' not found.");
                return;
            }

            entity.Physics?.ClearSpeed();
            Context.Respond($"Entity '{entity.DisplayName}' stopped");
        }

        [Command("delete", "Delete an entity.")]
        [Permission(MyPromoteLevel.SpaceMaster)]
        public void Delete(string entityName)
        {
            var name = entityName;
            if (string.IsNullOrEmpty(name))
                return;

            if (!Utilities.TryGetEntityByNameOrId(name, out IMyEntity entity))
            {
                Context.Respond($"Entity '{name}' not found.");
                return;
            }

            if (entity is IMyCharacter)
            {
                Context.Respond("You cannot delete characters.");
                return;
            }

            entity.Close();
            Context.Respond($"Entity '{entity.DisplayName}' deleted");
        }

        [Command("kill", "kill a player.")]
        [Permission(MyPromoteLevel.SpaceMaster)]
        public void Kill(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                Context.Respond($"you must specify a user to kill, you idiot");
                return;
            }

            if (!Utilities.TryGetEntityByNameOrId(name, out IMyEntity entity))
            {
                Context.Respond($"Entity '{name}' not found.");
                return;
            }

            if (entity is IMyCharacter)
            {
                var destroyable = entity as IMyDestroyableObject;
                destroyable.DoDamage(1000f, MyDamageType.Radioactivity, true);
                Context.Torch.CurrentSession?.Managers?.GetManager<IChatManagerServer>()?.SendMessageAsSelf
                    ($"{entity.DisplayName} was killed by an admin");
            }
        }


        [Command("find", "Find entities with the given text in their name.")]
        [Permission(MyPromoteLevel.SpaceMaster)]
        public void Find(string name)
        {
            if (string.IsNullOrEmpty(name))
                return;

            var sb = new StringBuilder("Found entities:\n");
            foreach (var entity in MyEntities.GetEntities())
            {
                if (entity is IMyVoxelBase voxel && (voxel.StorageName?.Contains(name, StringComparison.CurrentCultureIgnoreCase) ?? false))
                    sb.AppendLine($"{voxel.StorageName} ({entity.EntityId})");
                else if (entity?.DisplayName?.Contains(name, StringComparison.CurrentCultureIgnoreCase) ?? false)
                    //This can be null??? :keen:
                    sb.AppendLine($"{entity.DisplayName} ({entity.EntityId})");
            }

            Context.Respond(sb.ToString());
        }

        [Command("poweroff", "Power off entities with the given text in their name.")]
        [Permission(MyPromoteLevel.SpaceMaster)]
        public void PowerOff(string name)
        {

            if (string.IsNullOrEmpty(name))
                return;

            if (!Utilities.TryGetEntityByNameOrId(name, out IMyEntity entity))
            {
                Context.Respond($"Entity '{name}' not found.");
                return;
            }

            if (entity is IMyCharacter)
            {
                Context.Respond("Command do not work on characters.");
                return;
            }
            IMyCubeGrid grid = entity as MyCubeGrid;
            var blocks = new List<IMySlimBlock>();
            grid.GetBlocks(blocks, f => f.FatBlock != null && f.FatBlock is IMyFunctionalBlock
            && (f.FatBlock.BlockDefinition.TypeId == typeof(MyObjectBuilder_Reactor) ||
            f.FatBlock.BlockDefinition.TypeId == typeof(MyObjectBuilder_BatteryBlock) ||
            f.FatBlock.BlockDefinition.TypeId == typeof(MyObjectBuilder_SolarPanel)));
            var list = blocks.Select(f => (IMyFunctionalBlock)f.FatBlock).Where(f => f.Enabled).ToArray();
            foreach (var item in list)
            {
                item.Enabled = false;
            }
            Context.Respond($"Entity '{entity.DisplayName}' powered off");

        }

        [Command("poweron", "Power on entities with the given text in their name.")]
        [Permission(MyPromoteLevel.SpaceMaster)]
        public void PowerOn(string name)
        {

            if (string.IsNullOrEmpty(name))
                return;

            if (!Utilities.TryGetEntityByNameOrId(name, out IMyEntity entity))
            {
                Context.Respond($"Entity '{name}' not found.");
                return;
            }

            if (entity is IMyCharacter)
            {
                Context.Respond("Command do not work on characters.");
                return;
            }
            IMyCubeGrid grid = entity as MyCubeGrid;
            var blocks = new List<IMySlimBlock>();
            grid.GetBlocks(blocks, f => f.FatBlock != null && f.FatBlock is IMyFunctionalBlock
            && (f.FatBlock.BlockDefinition.TypeId == typeof(MyObjectBuilder_Reactor) ||
            f.FatBlock.BlockDefinition.TypeId == typeof(MyObjectBuilder_BatteryBlock) ||
            f.FatBlock.BlockDefinition.TypeId == typeof(MyObjectBuilder_SolarPanel)));
            var list = blocks.Select(f => (IMyFunctionalBlock)f.FatBlock).Where(f => !f.Enabled).ToArray();
            foreach (var item in list)
            {
                item.Enabled = true;
            }
            Context.Respond($"Entity '{entity.DisplayName}' powered on");

        }

        [Command("eject", "Ejects a specific player from any block they are seated in, or all players in the server if run with 'all'")]
        public void Eject(string player)
        {
            if (player.ToLower() == "all")
            {
                foreach (var ap in MySession.Static.Players.GetOnlinePlayers())
                {
                    var parent = ap.Character?.Parent;
                    if (parent == null)
                        continue;

                    if (parent is MyShipController c)
                    {
                        c.RemoveUsers(false);
                        //Context.Respond($"Ejected {parent.DisplayName} from seat.");
                        continue;
                    }
                    throw new Exception($"Unknown block parent type! {parent.GetType().FullName}");
                }

                Context.Respond("Ejected all players from seats");
            }
            else
            {
                var p = Utilities.GetPlayerByNameOrId(player);
                if (p == null)
                {
                    Context.Respond($"Could not find player {player}");
                    return;
                }

                var parent = p.Character?.Parent;
                if (parent == null)
                {
                    Context.Respond("Player is not seated.");
                    return;
                }

                if (parent is MyShipController c)
                {
                    c.RemoveUsers(false);
                    Context.Respond($"Ejected {p.DisplayName} from seat.");
                    return;
                }
                throw new Exception($"Unknown block parent type! {parent.GetType().FullName}");
            }
        }
    }
}
