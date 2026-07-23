using System;
using System.Collections.Generic;
using MessagePack;

namespace MakerBlendShapeSync
{
    public enum BlendShapeTargetScope
    {
        Unknown = 0,
        Character = 1,
        Clothing = 2,
        Accessory = 3,
        CharacterCoordinate = 4
    }

    [Serializable]
    [MessagePackObject(true)]
    public sealed class RendererScopeOverride
    {
        public string RendererPath { get; set; } = "";
        public string MeshName { get; set; } = "";
    }

    [Serializable]
    [MessagePackObject(true)]
    public sealed class BlendShapeRecord
    {
        public BlendShapeTargetScope TargetScope { get; set; } = BlendShapeTargetScope.Unknown;
        public int Coordinate { get; set; } = -1;
        public int Slot { get; set; } = -1;
        public string SlotRelativePath { get; set; } = "";
        public string RendererPath { get; set; } = "";
        public string RendererName { get; set; } = "";
        public string MeshName { get; set; } = "";
        public string ShapeName { get; set; } = "";
        public float Weight { get; set; }
    }

    [Serializable]
    [MessagePackObject(true)]
    public sealed class BlendShapeSyncData
    {
        public List<BlendShapeRecord> Records { get; set; } = new List<BlendShapeRecord>();
        public List<RendererScopeOverride> PerCoordinateRenderers { get; set; } =
            new List<RendererScopeOverride>();
    }
}
