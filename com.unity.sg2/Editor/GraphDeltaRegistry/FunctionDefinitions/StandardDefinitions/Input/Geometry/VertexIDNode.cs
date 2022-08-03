using UnityEditor.ShaderGraph.GraphDelta;

namespace UnityEditor.ShaderGraph.Defs
{
    internal class VertexIDNode : IStandardNode
    {
        public static string Name => "VertexID";
        public static int Version => 1;
        public static FunctionDescriptor FunctionDescriptor => new(
            Name,
            "Out = VertexID;",
            new ParameterDescriptor[]
            {
                new ParameterDescriptor("Out", TYPE.Float, GraphType.Usage.Out),
                new ParameterDescriptor("VertexID", TYPE.Float, GraphType.Usage.Local, REF.VertexID)
            }
        );

        public static NodeUIDescriptor NodeUIDescriptor => new(
            Version,
            Name,
            tooltip: "Gets the unique ID of each vertex.",
            categories: new string[2] { "Input", "Geometry" },
            hasPreview:false,
            synonyms: new string[0] { },
            displayName: "Vertex ID",
            parameters: new ParameterUIDescriptor[1] {
                new ParameterUIDescriptor(
                    name: "Out",
                    tooltip: "The unique ID of each vertex."
                )
            }
        );
    }
}
