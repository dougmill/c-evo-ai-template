using System;
using System.Collections.Generic;
using System.Diagnostics;
using CevoAILib;
using CevoAILib.Diplomacy;

namespace AI
{
    sealed class Empire : AEmpire
    {
        Random random = new Random();

        public Empire(int nationID, IntPtr serverPtr, IntPtr dataPtr, bool isNewGame)
            : base(nationID, serverPtr, dataPtr, isNewGame)
        {
        }

        protected override void NewGame()
        {
            Persistent.NewGame();
        }

        protected override void Resume()
        {
            Persistent.Resume();
        }

        protected override void OnTurn()
        {
            // check if research complete
            if (HadEvent__Turn(EmpireEvents.ResearchComplete))
            {
                if (random.Next(20) == 0)
                { // start military research
                    Blueprint.SetDomain__Turn(ModelDomain.Ground);
                    Blueprint.SetProperty__Turn(ModelProperty.Weapons, 3);
                    Blueprint.SetProperty__Turn(ModelProperty.Armor, 1);
                    Blueprint.SetProperty__Turn(ModelProperty.Mobility, Blueprint.MaximumWeight - Blueprint.Weight);
                    SetResearch__Turn(Advance.MilitaryResearch);
                }
                else
                { // set random research
                    Advance newResearch = Advance.None;
                    int count = 0;
                    for (Advance testResearch = Advance.FirstCommon; testResearch <= Advance.LastFuture; testResearch++)
                    {
                        if (CanSetResearch__Turn(testResearch))
                        {
                            count++;
                            if (random.Next(count) == 0)
                                newResearch = testResearch;
                        }
                    }
                    if (newResearch != Advance.None)
                        SetResearch__Turn(newResearch);
                }
            }

            // check cities
            foreach (City city in Cities)
            {
                if (city.Size < 4)
                    city.OptimizeExploitedLocations__Turn(ResourceWeights.MaxGrowth);
                else
                    city.OptimizeExploitedLocations__Turn(ResourceWeights.HurryProduction);
            }

            // move units
            foreach (Unit unit in Units)
            {
                Location[] neighborLocations = unit.Location.Neighbors;
                if (neighborLocations.Length > 0)
                    unit.MoveTo__Turn(neighborLocations[random.Next(neighborLocations.Length)]); // move unit to random adjacent location
            }

            // update saved city spy reports
            foreach (ForeignCity foreignCity in ForeignCities)
            {
                if (foreignCity.IsSpiedOut)
                {
                    PlayResult result = foreignCity.GetSpyReport(out CitySpyReport report);
                    Debug.Assert(result.OK);
                    Persistent.OldSpyReports[foreignCity.PersistentId] = report;
                }
            }
        }

        protected override void OnStealAdvance(Advance[] selection)
        {
            StealAdvance__Turn(selection[random.Next(selection.Length)]);
        }

        protected override void OnChanceToNegotiate(Phase situation, Nation Opponent, ref bool wantNegotiation, ref bool cancelTreatyIfRejected)
        {
            if (situation == Phase.BeginOfTurn) // start negotiation?
            {
                if (random.Next(30) == 0)
                    wantNegotiation = true;
            }
            if (situation == Phase.ForeignTurn) // accept contact for negotiation?
            {
                if (random.Next(2) == 0)
                    wantNegotiation = true;
            }
        }

        protected override void OnNegotiate(Negotiation negotiation)
        {
            if (negotiation.History.Count == 0 && RelationTo(negotiation.Opponent) < Relation.Alliance)
                negotiation.SetOurNextStatement(new SuggestTrade(new ITradeItem[] { new ChangeRelation(RelationTo(negotiation.Opponent) + 1) }, null)); // suggest better treaty
            if (negotiation.History.Count > 0 && negotiation.History[0].OpponentResponse is SuggestTrade)
            {
                SuggestTrade trade = negotiation.History[0].OpponentResponse as SuggestTrade;
                if ((trade.Offers.Length == 1 && trade.Wants.Length == 0 && trade.Offers[0] is ChangeRelation &&
                    ((ChangeRelation)trade.Offers[0]).NewRelation > RelationTo(negotiation.Opponent) ||
                    (trade.Offers.Length == 0 && trade.Wants.Length == 1 && trade.Wants[0] is ChangeRelation &&
                    ((ChangeRelation)trade.Wants[0]).NewRelation > RelationTo(negotiation.Opponent))))
                    negotiation.SetOurNextStatement(new AcceptTrade()); // accept better treaty
            }
        }

        protected override void OnForeignMove(MovingUnit unit)
        {
        }

        protected override void OnBeforeForeignAttack(MovingUnit attacker)
        {
        }

        protected override void OnAfterForeignAttack()
        {
        }

        protected override void OnBeforeForeignCapture(Nation nation, ICity city)
        {
            if (city.Nation == Us)
            {
                City myCity = Cities[city.PersistentId];
                Persistent.OldSpyReports[myCity.PersistentId] = myCity.ToSpyReport();
            }
        }

        protected override void OnAfterForeignCapture()
        {
        }

        protected override void OnUnitChanged(Location location)
        {
            // This could mean any of:
            //     1. A unit moved from here to somewhere you can't see.
            //     2. A unit moved to here from somewhere you can't see.
            //     3. A unit here died because the city supporting it was captured.
            //     4. A unit here attacked something you can't see.
            //     5. A unit here was attacked by something you can't see.
            //     6. A unit was expelled to here.
            // Unfortunately, the location is the only information the server provides.
        }

        protected override void OnDefeat(GameOverCause cause)
        {
        }

        protected override void OnVictory(GameOverCause cause)
        {
        }
    }
}
