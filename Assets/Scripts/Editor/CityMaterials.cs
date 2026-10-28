using System.Collections.Generic;
using UnityEngine;

namespace RacingPrototype.EditorTools
{
    /// <summary>Every shared material of the project, created once per build (URP Lit, physically based).</summary>
    public class CityMaterials
    {
        // Road & race furniture
        public Material Asphalt, LineWhite, LineYellow, Kerb, Paving, Plaza, Concrete, KerbStone, Gutter, Drain;
        public Material BarrierYellow, BarrierRed, Guardrail, Checker, CheckpointLine, GateBanner, GatePost, Truss;
        // Cars
        public readonly Dictionary<string, Material> Paint = new Dictionary<string, Material>();
        public Material CarTrim, Glass, Tyre, Rim, Chrome, HeadLight, TailLight, PlateDark, PlateLight, Grille, Carbon, Nitro, StripeWhite, StripeBlack;
        // Buildings
        public Material[] Plaster, Apartment, Office, Tower;
        public Material Storefront, Roof, BuildingTrim, Door, Gate;
        public Material[] RoofMetal, Awning, Sign;
        public Material Warehouse, WarehouseDoor;
        // Props & nature
        public Material Pole, LampGlow, PalmTrunk, PalmLeaf, Leaves, Trunk, Wood, Bin, Stone, Monument, Rail;
        public Material[] Container;
        public Material CraneRed, CraneWhite, ShipHull, ShipDeck, Water, Rock, Grass, Ground, Spectator, BillboardFace, BillboardFrame, Chevron, Sky;
    }
}
