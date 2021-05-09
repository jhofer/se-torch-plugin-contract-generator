using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Torch;
using Torch.API;
using Torch.API.Session;
using Torch.Session;
using Torch.API.Managers;
using Sandbox.ModAPI;
using Sandbox.Game.World;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Blocks;
using Torch.Utils;
using System.Reflection;
using VRageMath;

using Sandbox.Game.World.Generator;
using VRage.Network;
using Sandbox.Game.SessionComponents;
using VRage.Game.Definitions.SessionComponents;
using VRage.Library.Utils;
using Sandbox;
using Sandbox.Game.Contracts;
using Sandbox.ModAPI.Contracts;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.Components.Contracts;
using Sandbox.Game.GameSystems.BankingAndCurrency;
using Sandbox.Definitions;
using VRage;

namespace ContractGenerator
{


    public class ContractGeneratorPlugin : TorchPluginBase
    {
        public static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private Dictionary<long, List<MyCubeGrid>> contractTargets = new Dictionary<long,List<MyCubeGrid>>();
        /// <inheritdoc />
        public override void Init(ITorchBase torch)
        {
            base.Init(torch);
            Log.Info("ContractGeneratorPlugin: Init");

            var sessionManager = Torch.Managers.GetManager<TorchSessionManager>();
            if (sessionManager != null)
                sessionManager.SessionStateChanged += SessionChanged;
            else
                Log.Warn("No session manager loaded!");


        }


        private void GenerateContracts()
        {
         
            MyAPIGateway.Utilities.InvokeOnGameThread(() =>
            {
              
                var grids = MyEntities.GetEntities().Where((x) => (x as MyCubeGrid) != null).Select((x) => x as MyCubeGrid).ToList();
             
                foreach (var grid in grids)
                {
                    var contracBlocks = grid.CubeBlocks.Where((x) => (x.FatBlock as MyContractBlock) != null).Select((x) => x.FatBlock as MyContractBlock).ToList();
                   
                    var sncBlocks = contracBlocks.Where(x => x.CustomName.ToString().Contains("[CG]"));
               

                    foreach (var block in sncBlocks)
                    {
                        //block.OwnerId = Torch.Managers.Get
                        var location = block.CubeGrid.PositionComp.GetPosition();
                        Log.Info("Contract block at" + location.ToString());
                        var target = grids
                         .Where(x=>Ownership.GetOwnerType(x) == Ownership.OwnerType.NPC)
                            .Where(x => x.EntityId != grid.EntityId)
                            .Where(x => !x.BigOwners.Contains(block.OwnerId))
                            .Where(x => !contractTargets.ContainsKey(block.EntityId) || !contractTargets[block.EntityId].Contains(x))
                            .OrderBy((x) => Vector3D.Distance(x.PositionComp.GetPosition(), location))
                            .FirstOrDefault();

                        if (target == null)
                        {
                            Log.Info("No target found" + location.ToString());
                            continue;
                        }

                        var gridOwner = Ownership.GetOwner(target);

                        var player = MyAPIGateway.Players.GetPlayerControllingEntity(target);
                        Log.Warn("target belongs to "+player);
                        Action<List<MyObjectBuilder_Contract>> contractsCallback = (List<MyObjectBuilder_Contract> obj) =>
                        {



                            
                            var contractCount = obj.Count;
                            Log.Info("contract block has " + contractCount + " contracts");
                            if (obj.Count > 5) return;

                            

                            int rewardMoney = 100;
                            int startingDeposit = 0;
                            int durationInMin = 0;

                            long targetGridId = gridOwner; // target.EntityId;
                            double searchRadius = 100;
                            Log.Info("Generate contract for grid" + target.DebugName);
                            Action<MyContractCreationResults> contractCreatedCallback = ContractGenerated;
                            long contractId = 0;
                            long contractConditionId = 0;
                            object[] args = new object[] { block, rewardMoney, startingDeposit, durationInMin, targetGridId, searchRadius, contractId, contractConditionId };
                            //Invoke(block, "CreateCustomContractFind", args);



                            var result = GenerateCustomContract_Bounty(block, rewardMoney, startingDeposit, durationInMin, targetGridId, searchRadius, out contractId, out contractConditionId);
                            List<MyCubeGrid> targets;
                            if (!this.contractTargets.TryGetValue(block.EntityId, out targets)) {
                                targets = new List<MyCubeGrid>();
                                this.contractTargets[block.EntityId] = targets;
                            }
                            targets.Add(target);

                            Log.Warn("New Contract generated with result " + result);
                            //MyMultiplayer.RaiseEvent<MyContractBlock, MyContractCreationResults>(block, (Func<MyContractBlock, Action<MyContractCreationResults>>)(x => new Action<MyContractCreationResults>(this.ReceiveCreateContractResult)), component.GenerateCustomContract_Find(this, rewardMoney, startingDeposit,durationInMin, targetGridId, searchRadius, out long _, out long _), MyEventContext.Current.Sender);
                            //MyMultiplayer.RaiseEvent<MyContractBlock, MyContractCreationResults>(this, (Func<MyContractBlock, Action<MyContractCreationResults>>)(x => new Action<MyContractCreationResults>(x.ReceiveCreateContractResult)), component.GenerateCustomContract_Find(this, data.RewardMoney, data.StartingDeposit, data.DurationInMin, data.TargetGridId, data.SearchRadius, out long _, out long _), MyEventContext.Current.Sender);
                        };
                        Invoke<object>(block, "GetAvailableContracts", new object[] { contractsCallback });
                    }
                }

            });
        }

        private MyAddContractResultWrapper CreateContract(MyContractBlock startBlock,
              int rewardMoney,
              int startingDeposit,
              int durationInMin,
              long targetGridId,
              double searchRadius)
        {


            MySessionComponentContractSystem component = MySession.Static.GetComponent<MySessionComponentContractSystem>();
            if (component == null)
                throw new Exception("MySessionComponentContractSystem not found");
            var a = Sandbox.Game.Entities.MyEntities.GetEntityById(startBlock.EntityId);
            var balance = MyBankingSystem.GetBalance(startBlock.OwnerId);
            if (balance < (long)rewardMoney)
            {
                MyBankingSystem.ChangeBalance(startBlock.OwnerId, balance + rewardMoney);
            }

            MyContractSearch myContractSearch = new MyContractSearch(startBlock.EntityId, rewardMoney, startingDeposit, durationInMin, targetGridId, searchRadius);
            MyAddContractResultWrapper contractResultWrapper = component.AddContract((IMyContract)myContractSearch);
            MyBankingSystem.ChangeBalance(startBlock.OwnerId, balance);
            return contractResultWrapper;

        }


        private MyContractCreationResults GenerateCustomContract_Bounty(
              MyContractBlock startBlock,
              int rewardMoney,
              int startingDeposit,
              int durationInMin,
              long targetGridId,
              double searchRadius,
              out long contractId,
              out long contractConditionId)
        {
            contractId = 0L;
            contractConditionId = 0L;
            MySessionComponentEconomy component = MySession.Static?.GetComponent<MySessionComponentEconomy>();
            if (component == null)
                return MyContractCreationResults.Error_MissingKeyStructure;

            PropertyInfo prop = typeof(MySessionComponentEconomy).GetProperty("EconomyDefinition", BindingFlags.NonPublic | BindingFlags.Instance);

            MethodInfo getter = prop.GetGetMethod(nonPublic: true);
            MySessionComponentEconomyDefinition ecoDef = (MySessionComponentEconomyDefinition)getter.Invoke(component, null);

            //var ecoDef = Reflection.GetPrivateField<MySessionComponentEconomyDefinition>(component,"EconomyDefinition");

           // MyContractGenerator contractGenerator = new MyContractGenerator(ecoDef);
            MyTimeSpan now = MyTimeSpan.FromMilliseconds((double)MySandboxGame.TotalGamePlayTimeInMilliseconds);
            // if (MyBankingSystem.GetBalance(startBlock.OwnerId) < (long)rewardMoney)
            //     return MyContractCreationResults.Fail_NotEnoughFunds;
            MyContract contract;
            MyContractCreationResults customSearchContract = CreateCustomBountyContract(out contract, startBlock, rewardMoney, startingDeposit, durationInMin, targetGridId, now);
            if (customSearchContract != MyContractCreationResults.Success)
                return (MyContractCreationResults)customSearchContract;

               
            MySessionComponentContractSystem contractSystem = MySession.Static.GetComponent<MySessionComponentContractSystem>();
           
            Invoke<object>(contractSystem, "AddContract", new object[] { contract });
            //this.AddContract(contract);
            contractId = contract.Id;
            contractConditionId = contract.ContractCondition != null ? contract.ContractCondition.Id : 0L;
            //MyBankingSystem.ChangeBalance(startBlock.OwnerId, (long)-rewardMoney);
            return customSearchContract;
        }

       

        private int gameTicks = 0;
       
        public override void Update()
        {
            base.Update();
            gameTicks++;
            if (gameTicks > 60 * 10)
            {
                gameTicks = 0;
                GenerateContracts();
            }

        }

        public MyContractCreationResults CreateCustomBountyContract(
            out MyContract contract,
            MyContractBlock startBlock,
            int rewardMoney,
            int startingDeposit,
            int durationInMin,
            long targetIdentityId,
            MyTimeSpan now)
        {
            contract = (MyContract)null;
            MyContractHunt myContractHunt = new MyContractHunt();
            if (!(myContractHunt.GetDefinition() is MyContractTypeHuntDefinition definition))
                return MyContractCreationResults.Error;
            MyObjectBuilder_ContractHunt builderContractHunt = new MyObjectBuilder_ContractHunt();
            builderContractHunt.Id = MyEntityIdentifier.AllocateId(MyEntityIdentifier.ID_OBJECT_TYPE.CONTRACT);
            builderContractHunt.IsPlayerMade = false;
            builderContractHunt.State = MyContractStateEnum.Inactive;
            builderContractHunt.RewardMoney = (long)rewardMoney;
            builderContractHunt.RewardReputation = 0;
            builderContractHunt.StartingDeposit = (long)startingDeposit;
            builderContractHunt.FailReputationPrice = 0;
            builderContractHunt.StartFaction = 0L;
            builderContractHunt.StartStation = 0L;
            builderContractHunt.StartBlock = startBlock.EntityId;
            builderContractHunt.Target = targetIdentityId;
            builderContractHunt.RemarkPeriod = MyTimeSpan.FromSeconds((double)definition.RemarkPeriodInS).Ticks;
            builderContractHunt.RemarkVariance = definition.RemarkVariance;
            builderContractHunt.KillRange = definition.KillRange;
            builderContractHunt.KillRangeMultiplier = definition.KillRangeMultiplier;
            builderContractHunt.ReputationLossForTarget = definition.ReputationLossForTarget;
            builderContractHunt.RewardRadius = definition.RewardRadius;
            builderContractHunt.Creation = now.Ticks;
            builderContractHunt.RemainingTimeInS = new double?(MyTimeSpan.FromMinutes((double)durationInMin).Seconds);
            builderContractHunt.TicksToDiscard = new int?();
            myContractHunt.Init((MyObjectBuilder_Contract)builderContractHunt);
            contract = (MyContract)myContractHunt;
            return MyContractCreationResults.Success;
        }

        private bool DoMatchParamType(MethodInfo m, object[] args)
        {
            var paramTypes = args.Select(x => x.GetType()).ToList();
            var methodParamTypes = m.GetParameters().Select(x => x.ParameterType).ToList();
            for (int i = 0; i < methodParamTypes.Count; i++)
            {
                var argType = paramTypes[i];
                var methodType = methodParamTypes[i];
                if (paramTypes[i] != methodParamTypes[i] && !methodType.IsAssignableFrom(argType))
                {
                    return false;
                }
            }
            return true;

        }

        private T Invoke<T>(object target, string name, object[] args)
        {
            Log.Info("Invoke " + name + " on " + target.ToString());
            var allMethods = target.GetType().GetMethods(BindingFlags.Instance |
                            BindingFlags.NonPublic |
                            BindingFlags.Public);
            var firstFilterMethods = allMethods.Where(m => m.Name == name && m.DeclaringType == target.GetType() && m.GetParameters().Length == args.Length).ToList();

            var paramTypes = args.Select(x => x.GetType()).ToList();
            List<MethodInfo> methods = new List<MethodInfo>();
            foreach (var m in firstFilterMethods)
            {
                if (DoMatchParamType(m, args))
                {
                    methods.Add(m);
                }

            }


            int methodCount = methods.Count;
            if (methodCount > 1)
            {
                var found = String.Join(", ", methods.Select(m => m.ToString()).ToList());
                throw new Exception("Multiple matching methods found " + name + " found: " + found);
            }
            var method = methods.FirstOrDefault();

            if (method == null)
            {

                throw new Exception("Method " + name + " on " + target.ToString() + " not found");

            }
            return (T)method.Invoke(target, args);
        }

        private void ContractGenerated(MyContractCreationResults obj)
        {
            // erro: Fail_NotAnOwnerOfBlock
            Log.Warn("New Contract generated with result" + obj.ToString());
        }

        private void SessionChanged(ITorchSession session, TorchSessionState newState)
        {

            Log.Info("Session-State is now " + newState);
            if (newState.Equals(TorchSessionState.Loaded))
            {
                GenerateContracts();
            }

        }
    }

}
