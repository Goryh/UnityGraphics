using Unity.GraphToolsFoundation.Editor;
using UnityEditor.ShaderGraph.GraphDelta;

namespace UnityEditor.ShaderGraph.GraphUI
{
    /// <summary>
    /// AbstractStaticPortPart is a node part that reads/writes a static port on a node.
    /// </summary>
    abstract class AbstractStaticPortPart : BaseModelViewPart
    {
        /// <summary>
        /// Update this part's UI using the given port reader.
        /// </summary>
        /// <param name="reader">Reader for the port associated with this part.</param>
        protected abstract void UpdatePartFromPortReader(PortHandler reader);

        protected string m_PortName;
        protected string m_PortDisplayName;

        protected AbstractStaticPortPart(string name, GraphElementModel model, ModelView ownerElement, string parentClassName, string portName, string portDisplayName)
            : base(name, model, ownerElement, parentClassName)
        {
            m_PortName = portName;
            m_PortDisplayName = portDisplayName;
        }

        protected override void UpdatePartFromModel()
        {
            if (m_Model is not SGNodeModel model) return;
            if (!model.graphDataOwner.TryGetNodeHandler(out var nodeReader)) return;
            var port = nodeReader.GetPort(m_PortName);
            if (port != null)
                UpdatePartFromPortReader(port);
        }
    }
}
