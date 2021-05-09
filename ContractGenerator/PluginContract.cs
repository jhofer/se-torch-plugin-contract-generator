using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage.Game;
using VRage.Game.ModAPI;

namespace ContractGenerator
{
    class PluginContract : IMyContractCustom
    {
        public MyDefinitionId DefinitionId => new MyDefinitionId();

        public long? EndBlockId => throw new NotImplementedException();

        public string Name => throw new NotImplementedException();

        public string Description => throw new NotImplementedException();

        public int ReputationReward => throw new NotImplementedException();

        public int FailReputationPrice => throw new NotImplementedException();

        public long StartBlockId => throw new NotImplementedException();

        public int MoneyReward => throw new NotImplementedException();

        public int Collateral => throw new NotImplementedException();

        public int Duration => throw new NotImplementedException();

        public Action<long> OnContractAcquired { get ; set; }
        public Action OnContractSucceeded { get; set; }
        public Action OnContractFailed { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    }
}
