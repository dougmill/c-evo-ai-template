using System;
using System.Collections.Generic;
using System.Diagnostics;
using AI;

namespace CevoAILib.Diplomacy
{
    interface ITradeItem
    {
        int Code { get; }
    }

    class ItemOfChoice : ITradeItem
    {
        public int Code => Protocol.opChoose;
        public ItemOfChoice() { }

        public override string ToString() => "ItemOfChoice";
    }

    class CopyOfMap : ITradeItem
    {
        public int Code => Protocol.opMap;
        public CopyOfMap() { }

        public override string ToString() => "CopyOfMap";
    }

    class CopyOfDossier : ITradeItem
    {
        public readonly Nation Nation;
        public readonly int TurnOfReport;
        public CopyOfDossier(Nation nation) => (Nation, TurnOfReport) = (nation, 0); // turn will be set by server
        public CopyOfDossier(Nation nation, int turnOfReport) => (Nation, TurnOfReport) = (nation, turnOfReport);
        public int Code => Protocol.opCivilReport + (((IId) Nation.Id).Index << 16) + TurnOfReport;

        public override string ToString() => $"CopyOfDossier {Nation}";
    }

    class CopyOfMilitaryReport : ITradeItem
    {
        public readonly Nation Nation;
        public readonly int TurnOfReport;
        public CopyOfMilitaryReport(Nation nation) => (Nation, TurnOfReport) = (nation, 0); // turn to be set by server
        public CopyOfMilitaryReport(Nation nation, int turnOfReport) => (Nation, TurnOfReport) = (nation, turnOfReport);
        public int Code => Protocol.opMilReport + (((IId) Nation.Id).Index << 16) + TurnOfReport;

        public override string ToString() => $"CopyOfMilitaryReport {Nation}";
    }

    class Payment : ITradeItem
    {
        public readonly int Amount;
        public Payment(int amount) => Amount = amount;
        public int Code => Protocol.opMoney + Amount;

        public override string ToString() => $"Payment {Amount}";
    }

    class TeachAdvance : ITradeItem
    {
        public readonly Advance Advance;
        public TeachAdvance(Advance advance) => Advance = advance;
        public int Code => Protocol.opTech + (int) Advance;

        public override string ToString() => $"TeachAdvance {Advance}";
    }

    class TeachAllAdvances : ITradeItem
    {
        public int Code => Protocol.opAllTech;
        public TeachAllAdvances() { }

        public override string ToString() => "TeachAllAdvances";
    }

    class TeachModel : ITradeItem
    {
        public readonly ModelBase Model;
        private readonly IId ModelId;
        public TeachModel(Model model) => (Model, ModelId) = (model, model.Id);
        public TeachModel(ForeignModel model)  => (Model, ModelId) = (model, model.OwnersModelId);
        public int Code => Protocol.opModel + ModelId.Index;

        public override string ToString() => $"TeachModel {Model}";
    }

    class TeachAllModels : ITradeItem
    {
        public int Code => Protocol.opAllModel;
        public TeachAllModels() { }

        public override string ToString() => "TeachAllModels";
    }

    class ColonyShipPartLot : ITradeItem
    {
        public readonly Building PartType;
        public readonly int Number;
        public ColonyShipPartLot(Building partType, int number) => (PartType, Number) = (partType, number);
        public int Code => Protocol.opShipParts + ((PartType - Building.ColonyShipComponent) << 16) + Number;

        public override string ToString() => $"{PartType} x{Number}";
    }

    class ChangeRelation : ITradeItem
    {
        public readonly Relation NewRelation;
        public ChangeRelation(Relation newRelation) => NewRelation = newRelation;
        public int Code => Protocol.opTreaty + (int) NewRelation;

        public override string ToString() => $"ChangeRelation {NewRelation}";
    }

    interface IStatement
    {
        int Command { get; }
    }

    class Notice : IStatement
    {
        public Notice() { }
        public int Command => Protocol.scDipNotice;

        public override string ToString() => "Notice";
    }

    class AcceptTrade : IStatement
    {
        public AcceptTrade() { }
        public int Command => Protocol.scDipAccept;

        public override string ToString() => "AcceptTrade";
    }

    class CancelTreaty : IStatement
    {
        public CancelTreaty() { }
        public int Command => Protocol.scDipCancelTreaty;

        public override string ToString() => "CancelTreaty";
    }

    class SuggestTrade : IStatement
    {
        public int Command => Protocol.scDipOffer;

        public readonly ITradeItem[] Offers;
        public readonly ITradeItem[] Wants;

        public SuggestTrade(ITradeItem[] offers, ITradeItem[] wants)
        {
            Offers = offers ?? new ITradeItem[0];
            Wants = wants ?? new ITradeItem[0];
        }

        public int[] Data
        {
            get
            {
                int[] data = new int[14];
                data[0] = Offers.Length;
                for (int i = 0; i < Offers.Length; i++)
                    data[2 + i] = Offers[i].Code;
                data[1] = Wants.Length;
                for (int i = 0; i < Wants.Length; i++)
                    data[2 + Offers.Length + i] = Wants[i].Code;
                return data;
            }
        }

        public override string ToString()
        {
            string offerString = Offers.Length > 0 ? string.Join(" + ", (IEnumerable<ITradeItem>) Offers) : "nothing";
            string wantString = Wants.Length > 0 ? string.Join(" + ", (IEnumerable<ITradeItem>) Wants) : "nothing";
            return "SuggestTrade " + offerString + " for " + wantString;
        }
    }

    class SuggestEnd : SuggestTrade
    {
        public SuggestEnd() : base(null, null) { }

        public override string ToString() => "SuggestEnd";
    }

    class Break : IStatement
    {
        public Break() { }
        public int Command => Protocol.scDipBreak;

        public override string ToString() => "Break";
    }

    static class StatementFactory
    {
        static ITradeItem TradeItemFromCode(AEmpire empire, Nation source, int code)
        {
            switch (code & Protocol.opMask)
            {
                case Protocol.opChoose: return new ItemOfChoice();
                case Protocol.opMap: return new CopyOfMap();
                case Protocol.opCivilReport: return new CopyOfDossier(new Nation(empire, new NationId((code >> 16) & 0xFF)), code & 0xFFFF);
                case Protocol.opMilReport: return new CopyOfMilitaryReport(new Nation(empire, new NationId((code >> 16) & 0xFF)), code & 0xFFFF);
                case Protocol.opMoney: return new Payment(code & 0xFFFF);
                case Protocol.opTech: return new TeachAdvance((Advance)(code & 0xFFFF));
                case Protocol.opAllTech: return new TeachAllAdvances();
                case Protocol.opAllModel: return new TeachAllModels();
                case Protocol.opShipParts: return new ColonyShipPartLot((Building)((int)Building.ColonyShipComponent + ((code >> 16) & 0xFF)), code & 0xFFFF);
                case Protocol.opTreaty: return new ChangeRelation((Relation)(code & 0xFFFF));

                case Protocol.opModel:
                    {
                        if (source == empire.Us)
                            return new TeachModel(empire.Models[new ModelId((short) (code & 0xFFFF))]);
                        else
                        {
                            foreach (ForeignModel model in empire.ForeignModels)
                            {
                                if (model.Nation == source && ((IId) model.OwnersModelId).Index == (code & 0xFFFF))
                                    return new TeachModel(model);
                            }
                        }
                        throw new Exception("Error in StatementFactory: Foreign model not found!");
                    }

                default: throw new Exception("Error in StatementFactory: Not a valid trade item code!");
            }
        }

        public static unsafe IStatement OpponentStatementFromCommand(AEmpire empire, Nation opponent, int command, int* rawStream)
        {
            switch (command)
            {
                case Protocol.scDipNotice: return new Notice();
                case Protocol.scDipAccept: return new AcceptTrade();
                case Protocol.scDipCancelTreaty: return new CancelTreaty();
                case Protocol.scDipOffer:
                    {
                        if (rawStream[0] + rawStream[1] == 0)
                            return new SuggestEnd();
                        else
                        {
                            ITradeItem[] offers = new ITradeItem[rawStream[0]];
                            if (rawStream[0] > 0)
                            {
                                for (int i = 0; i < rawStream[0]; i++)
                                    offers[i] = TradeItemFromCode(empire, opponent, rawStream[2 + i]);
                            }
                            ITradeItem[] wants = new ITradeItem[rawStream[1]];
                            if (rawStream[1] > 0)
                            {
                                for (int i = 0; i < rawStream[1]; i++)
                                    wants[i] = TradeItemFromCode(empire, empire.Us, rawStream[2 + rawStream[0] + i]);
                            }
                            return new SuggestTrade(offers, wants);
                        }
                    }
                case Protocol.scDipBreak: return new Break();
                default: throw new Exception("Error in StatementFactory: Not a negotiation!");
            }
        }
    }

    struct ExchangeOfStatements
    {
        public IStatement OurStatement;
        public IStatement OpponentResponse;

        public ExchangeOfStatements(IStatement ourStatement, IStatement opponentResponse)
        {
            OurStatement = ourStatement;
            OpponentResponse = opponentResponse;
        }
    }

    sealed class Negotiation
    {
        private readonly AEmpire TheEmpire;
        public readonly Phase Situation;
        public readonly Nation Opponent;
        public readonly List<ExchangeOfStatements> History = new List<ExchangeOfStatements>(); // sorted from new to old, newest at index 0
        public IStatement OurNextStatement { get; private set; }

        public Negotiation(AEmpire empire, Phase negotiationSituation, Nation opponent)
        {
            TheEmpire = empire;
            Situation = negotiationSituation;
            Opponent = opponent;
        }

        public unsafe PlayResult SetOurNextStatement(IStatement statement)
        {
            PlayResult result = PlayResult.Success;
            if (statement is SuggestTrade trade)
            {
                if (trade.Offers.Length > 2 || trade.Wants.Length > 2)
                    result = new PlayResult(PlayError.RulesViolation);

                // check model owners
                foreach (ITradeItem offer in trade.Offers)
                {
                    if (offer is TeachModel teachModel && teachModel.Model.Nation != TheEmpire.Us)
                        result = new PlayResult(PlayError.RulesViolation);
                }
                foreach (ITradeItem want in trade.Wants)
                {
                    if (want is TeachModel teachModel && teachModel.Model.Nation != Opponent)
                        result = new PlayResult(PlayError.RulesViolation);
                }

                if (result.OK)
                {
                    fixed (int* tradeData = trade.Data)
                    {
                        result = TheEmpire.TestPlay(statement.Command, 0, tradeData);
                    }
                }
            }
            else
                result = TheEmpire.TestPlay(statement.Command);
            if (result.OK)
                OurNextStatement = statement;
            return result;
        }
    }
}
