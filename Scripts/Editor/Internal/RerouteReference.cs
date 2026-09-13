using UnityEditor;
using UnityEngine;

namespace XNodeEditor.Internal {
	/// <summary> 指向某条连接上某个重路由点的引用：端口 + 连接索引 + 点索引 三元组定位 </summary>
	public struct RerouteReference {
		public XNode.NodePort port;
		public int connectionIndex;
		public int pointIndex;

		/// <summary> 绑定到指定端口指定连接的指定重路由点 </summary>
		public RerouteReference(XNode.NodePort port, int connectionIndex, int pointIndex) {
			this.port = port;
			this.connectionIndex = connectionIndex;
			this.pointIndex = pointIndex;
		}

		/// <summary> 在点索引处插入新点，其后各点顺延 </summary>
		public void InsertPoint(Vector2 pos) {
			if (port == null || port.node == null) return;
			Undo.RecordObject(port.node, "Insert Reroute Point");
			port.GetReroutePoints(connectionIndex).Insert(pointIndex, pos);
			EditorUtility.SetDirty(port.node);
		}
		/// <summary> 把该点移动到新位置 </summary>
		public void SetPoint(Vector2 pos) {
			if (port == null || port.node == null) return;
			port.GetReroutePoints(connectionIndex) [pointIndex] = pos;
			EditorUtility.SetDirty(port.node);
		}
		/// <summary> 删除该点，其后各点前移 </summary>
		public void RemovePoint() {
			if (port == null || port.node == null) return;
			Undo.RecordObject(port.node, "Remove Reroute Point");
			port.GetReroutePoints(connectionIndex).RemoveAt(pointIndex);
			EditorUtility.SetDirty(port.node);
		}
		/// <summary> 取该点当前坐标 </summary>
		public Vector2 GetPoint() { return port.GetReroutePoints(connectionIndex) [pointIndex]; }
	}
}
