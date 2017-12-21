using System;
using System.Collections.Generic;
using CevoAILib;

namespace AI
{
    sealed class Unit : AUnit
    {
        public Unit(AEmpire empire, UnitId id)
            : base(empire, id)
        {
        }
    }
}
