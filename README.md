# xNode

xNode 是一个用于 Unity 的节点图框架，提供运行时节点、图容器、端口连接和可扩展的节点图编辑器。它适合搭建状态机、对话系统、行为树、数据处理流程和其他自定义图结构。

[![许可证](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE.md)
[![OpenUPM](https://img.shields.io/npm/v/com.github.siccity.xnode?label=OpenUPM&registry_uri=https://package.openupm.com)](https://openupm.com/packages/com.github.siccity.xnode/)

## 当前版本

- 包名：`com.github.siccity.xnode`
- 版本：`1.8.0`
- 最低 Unity 版本：`2022.3`
- 运行时程序集：`XNode`
- 编辑器程序集：`XNodeEditor`，仅在 Editor 平台编译
- 依赖：无第三方运行时插件

当前源码以 Unity 2022.3 为最低目标。README 中的旧版 Unity 5.3、2018.3 兼容声明已不再适用。

## 功能概览

- 使用 `ScriptableObject` 保存图和节点，支持 Unity 资产序列化。
- 使用 `[Input]` 和 `[Output]` 从字段自动生成静态端口。
- 支持多连接、单连接覆盖、动态端口和数组/List 动态端口列表。
- 支持 `None`、`Inherited`、`Strict`、`InheritedInverse`、`InheritedAny` 五种类型约束。
- 节点图编辑器支持节点创建、复制、删除、拖线、重路由点、框选、网格吸附和自定义节点编辑器。
- 运行时程序集不引用 UnityEditor；编辑器代码通过独立程序集隔离。
- 端口类型和字段反射数据在进程内缓存，避免运行时反复扫描类型。

## 安装

### Unity Package Manager Git 依赖

在项目的 `Packages/manifest.json` 中加入：

```json
{
  "dependencies": {
    "com.github.siccity.xnode": "https://github.com/Siccity/xNode.git"
  }
}
```

安装 Git URL 依赖需要本机已安装 Git。为了保证构建可复现，正式项目应将 URL 固定到已审核的提交；当前仓库没有发布 tag，不能假设某个 tag 已存在。

### OpenUPM

```bash
openupm add com.github.siccity.xnode
```

### 作为源码导入

也可以将仓库放入项目的 `Packages` 目录，或直接复制到 `Assets` 下。使用 Assembly Definition 时：

- 运行时代码引用 `XNode`。
- 自定义 Editor 代码引用 `XNodeEditor`。
- 不要让运行时程序集引用 `XNodeEditor` 或 UnityEditor。

## 快速开始

### 创建图和节点

节点图继承 `NodeGraph`，节点继承 `Node`。节点可以作为图资产的子资产保存。

```csharp
using XNode;

public class SampleGraph : NodeGraph
{
}

public class AddNode : Node
{
    [Input] public float left;
    [Input] public float right;
    [Output] public float result;

    public override object GetValue(NodePort port)
    {
        if (port == GetOutputPort(nameof(result)))
            return GetInputValue<float>(nameof(left), left)
                 + GetInputValue<float>(nameof(right), right);

        return null;
    }
}
```

编辑器中创建 `SampleGraph` 资产后，可以通过节点图窗口的右键菜单创建 `AddNode`。也可以通过代码创建节点：

```csharp
SampleGraph graph = ScriptableObject.CreateInstance<SampleGraph>();
AddNode node = graph.AddNode<AddNode>();
OtherNode otherNode = graph.AddNode<OtherNode>();
node.position = new UnityEngine.Vector2(100, 100);
node.GetOutputPort(nameof(AddNode.result))
    .Connect(otherNode.GetInputPort(nameof(OtherNode.input)));

public class OtherNode : Node
{
    [Input] public float input;
}
```

推荐从输出端调用 `Connect`。当前实现也会校验调用方向、端口类型和双方的类型约束，并保持连接记录的双向一致性。

### 端口属性

```csharp
[Input(
    backingValue: Node.ShowBackingValue.Unconnected,
    connectionType: Node.ConnectionType.Override,
    typeConstraint: Node.TypeConstraint.Inherited)]
public ScriptableObject asset;

[Output(
    backingValue: Node.ShowBackingValue.Never,
    connectionType: Node.ConnectionType.Multiple,
    typeConstraint: Node.TypeConstraint.Strict)]
public float value;
```

`ConnectionType.Override` 表示新连接会替换旧连接，`Multiple` 表示允许多条连接。类型约束会同时参与编辑器连接过滤和运行时 `Connect` 校验。

### 动态端口

普通动态端口：

```csharp
NodePort input = node.AddDynamicInput(typeof(float), fieldName: "extraInput");
NodePort output = node.AddDynamicOutput(typeof(float), fieldName: "extraOutput");
node.RemoveDynamicPort(input);
```

数组或 `List<T>` 可以声明为动态端口列表：

```csharp
[Input(dynamicPortList = true)]
public float[] inputs;
```

编辑器会为列表元素生成形如 `inputs 0`、`inputs 1` 的动态端口，并在增删、重排时同步数组数据。列表元素类型必须能被 Unity 序列化。

## 运行时求值

节点输出值通过 `Node.GetValue(NodePort port)` 计算。输入节点可以使用：

- `GetInputValue<T>(fieldName, fallback)`：读取第一个有效连接，否则返回 fallback。
- `GetInputValues<T>(fieldName, fallback)`：读取全部有效连接，否则返回 fallback。
- `NodePort.TryGetInputValue<T>`：读取并返回类型匹配状态。
- `NodePort.GetOutputValue()`：请求输出端口的计算值。

如果图中存在循环连接，求值会被保护逻辑截断并输出警告。节点的 `GetValue` 应避免依赖副作用，并明确处理输出端口名称不匹配的情况。

## 编辑器扩展

可以通过继承 `NodeEditor` 和 `NodeGraphEditor` 自定义节点或图的绘制行为：

```csharp
using XNodeEditor;

[CustomNodeEditor(typeof(AddNode))]
public class AddNodeEditor : NodeEditor
{
    public override void OnBodyGUI()
    {
        serializedObject.Update();
        NodeEditorGUILayout.PropertyField(serializedObject.FindProperty("left"));
        NodeEditorGUILayout.PropertyField(serializedObject.FindProperty("right"));
        serializedObject.ApplyModifiedProperties();
    }
}
```

Editor 扩展必须放在 Editor 目录或 Editor-only Assembly Definition 中，并引用 `XNodeEditor`。复杂连接规则可重写 `NodeGraphEditor.CanConnect`，但仍建议保持端口自身的类型约束有效。

## 资产和序列化约定

- `NodeGraph.nodes` 保存图内节点引用；节点通常作为图资产的子资产存在。
- 连接序列化为对端节点引用、端口字段名和重路由点，运行时端口对象只作为缓存。
- 修改字段名时应使用 Unity 的 `[FormerlySerializedAs]`，便于端口连接迁移。
- 修改端口类型、方向或连接策略后，插件会同步端口定义并清理不再合法的动态列表连接。
- 不要直接修改 `NodePort` 内部连接数据；建立和断开连接应使用 `Connect`、`Disconnect`、`ClearConnections`。

## 测试

测试位于 `Tests`，测试程序集仅包含 Editor 平台的 EditMode 测试。使用 Unity 2022.3 打开包含本包的项目后，在 Test Runner 中选择 EditMode 运行。

当前测试覆盖节点图复制、节点生命周期、连接双向一致性、Override 连接、类型约束、动态端口、循环求值保护和类型缓存边界。完整 Unity batchmode 验证需要一个实际的 Unity 项目，而本仓库本身是独立包源码，不包含 `ProjectSettings`。

## 兼容性说明

当前分支以 Unity 2022.3 为最低目标。编辑器代码使用 UnityEditor 的 IMGUI 和 AdvancedDropdown API，并通过版本宏适配部分 Unity 6 API 差异。

1.8.0 包包含连接 API 和端口类型约束相关调整。升级已有项目时，应重新检查自定义节点编辑器、直接调用 `Connect` 的代码以及自定义类型约束逻辑。

## 相关链接

- [GitHub 仓库](https://github.com/Siccity/xNode)
- [Wiki 文档](https://github.com/Siccity/xNode/wiki)
- [Issues](https://github.com/Siccity/xNode/issues)
- [Discord 社区](https://discord.gg/qgPrHv4)
- [xNodeGroups](https://github.com/Siccity/xNodeGroups)

## 许可证

本项目使用 MIT 许可证，详见 [LICENSE.md](LICENSE.md)。
