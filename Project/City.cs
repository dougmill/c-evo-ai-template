using System;
using System.Collections.Generic;
using CevoAILib;

namespace AI
{
    sealed class City : ACity
    {
        public City(AEmpire empire, CityId id)
            : base(empire, id)
        {
        }
    }

    sealed class ForeignCity : AForeignCity
    {
        public ForeignCity(AEmpire empire, ForeignCityId id)
            : base (empire, id)
        {
        }
    }
}
