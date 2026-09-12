using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using XNode;

namespace XNode {
	/// <summary> 在场景中实例化一个节点图，使图内可以引用场景对象 </summary>
	public class SceneGraph : MonoBehaviour {
		public NodeGraph graph;
	}

	/// <summary> 继承本类可为指定图类型创建强类型的 SceneGraph </summary>
	/// <example>
	/// <code>
	/// public class MySceneGraph : SceneGraph&lt;MyGraph&gt; {
	///
	/// }
	/// </code>
	/// </example>
	public class SceneGraph<T> : SceneGraph where T : NodeGraph {
		public new T graph { get { return base.graph as T; } set { base.graph = value; } }
	}
}
