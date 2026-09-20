# FaceWeightBinder 0.2.0.0 — 初版发布

为带有 FaceWeightProcess 标记、原版面部骨骼权重的衣物和饰品绑定当前角色的面部骨骼。

## 本版收口

- 默认关闭详细绑定扫描、参考搜索和坐标诊断日志；关闭时不生成这些报告。
- 默认关闭状态快照，快捷键和 API 请求均受开关控制。升级旧版本后，仅保留的快捷键设置不会自动启用快照。
- 保留绑定失败、兼容调用失败等必要警告和错误。
- 保留原脸候选排除与资产内部相对空间缓存修正。
- Unity authoring DLL 保持 0.1.0.0；本版运行时收口无需重新打包已有有效资产。

## 安装

选择对应游戏的压缩包，将 BepInEx 文件夹合并到游戏目录。更新时替换原运行时 FaceWeightBinder.dll，避免重复安装。

KK 和 KKS 的运行时文件都叫 FaceWeightBinder.dll，但不能混用或同时安装。不要把 Unity authoring DLL 放入游戏。

配置文件：BepInEx/config/tomtom.faceweightbinder.cfg。
Diagnostics 下的 Verbose logging 和 Enable snapshots 默认均为 false。
只有显式启用 Enable snapshots，Left Ctrl + Left Shift + F8 才导出诊断数据。

## Unity 饰品制作顺序（必须遵守）

**必须先添加并配置 Cha Acc 组件，再添加 Face Weight Process 权重组件。**
反过来添加已观察到游戏中模型放大 **100 倍** 的问题。
完成两个组件配置后，再点击 Capture Renderer Bindings、Validate Bindings，
保存 prefab 后打包。此项是当前已观察到的制作流程限制，缩放触发机制仍在核查。

## 依赖核查

| 编译参考 | KK | KKS |
| --- | --- | --- |
| BepInEx | 5.4.23.5 | 5.4.23.4 |
| 0Harmony | 2.9.0.0 | 2.9.0.0 |
| 游戏 API | KKAPI 1.46.1.0 | KKSAPI 1.42.2.0 |
| .NET 目标 | 3.5 | 4.6.2 |

以上是 DLL 元数据中的编译参考版本，不是配置中的精确版本限制。插件以 marco.kkapi GUID 声明必须安装 API，未设置版本条件。BepInEx、Harmony 和游戏 API 引用均无强名称公钥标记；代码没有精确版本判断，也没有从作者本机绝对路径加载 DLL。实际兼容仍取决于运行环境是否提供所需类型、方法和事件，尚未验证所有历史版本或确定最低 API 版本。

AccessoryBoneBinder、MakerBlendShapeSync、ModBoneImplantor、Coordinate Load Option 均为可选依赖。前两者及 Coordinate Load Option 通过已加载插件探测接口，不要求额外复制这些 DLL；ABMX、DBDE 无直接程序集引用。共享代码编入插件，无需另装 TomTom.KKMod.Shared.dll。

KKS 构建默认参考仓库内 1.42.2 文件，可通过 KKSApiReferencePath 改用其他兼容参考；KK 对应 KKApiReferencePath。这些只是构建参数。SpecificVersion=false 不等于任意版本都能运行。

## 验证与边界

- KK/KKS Release 编译通过，零警告、零错误。
- 两份成品通过版本、诊断关闭默认值、快照入口开关、依赖声明和本机路径字符串检查。
- Unity 5.6 原脸候选/相对空间矩阵回归：12 项通过。
- 资产权重及模型调整由用户在本轮收口前验收；本次发布 DLL 尚未重新进行两款游戏内完整验收。
- 自定义面罩需要正确权重；插件不会自动制作/同步缺失的表情形态键，也不保证任意模型或极端捏脸都不穿模。

发布包不包含游戏、Unity、BepInEx、Harmony 或 API DLL，沿用对应游戏环境中的依赖。
