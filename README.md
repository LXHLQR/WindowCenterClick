# WindowCenterClick
﻿窗口居中程序
====================

适用：Windows 10（较新版本）/ Windows 11，.NET Framework 4.8。
这是源码及自动构建启动包，不包含已编译的 EXE。当前制作环境为 Linux，
没有 C# 编译器或 Windows 桌面，尚未进行实际编译和 Windows 运行验证。

快速使用
--------
1. 将 ZIP 中的整个 WindowCenterClick 文件夹解压到可写目录。
2. 双击 Start.cmd。第一次会调用本机的 .NET Framework C# 编译器，
   生成 WindowCenterClick.exe 后打开界面。无需输入命令，不会联网下载组件。
3. 点击“选择窗口并居中” → 工具暂时隐藏 → 左键单击目标应用窗口。
4. 自动识别窗口和所属进程，将窗口居中到其所在显示器的工作区。
5. 后续直接双击 WindowCenterClick.exe 即可，也可继续使用 Start.cmd。

请点击“已经打开的应用窗口”，不是桌面快捷方式图标或任务栏图标。
本工具不会根据桌面图标启动应用。目标窗口应在屏幕上可见。
可以为 WindowCenterClick.exe 创建桌面快捷方式；保留旁边的
WindowCenterClick.exe.config 文件。Windows 11 可在“显示更多选项”中
使用“发送到 → 桌面快捷方式”。

窗口行为
--------
• 鼠标点击窗口中的控件时，通过 GA_ROOT 找到所属顶层窗口。
• 此次左键选择的按下和抬起会被拦截，避免触发目标窗口按钮。
  只会在选择期间启用鼠标钩子，不记录键盘输入或鼠标历史，不联网。
• 选择期间右键取消；Esc 在临时快捷键注册成功时也可取消。
  若 Esc 被其他程序占用，请用右键。30 秒未完成选择会自动取消。
• 最大化窗口先还原，再居中。无边框全屏窗口应先退出全屏。
• 默认保留正常窗口尺寸；勾选“窗口过大时，尝试缩小到可用区域”后，
  仅对带可调整大小样式的超大窗口请求缩小，不改变其他窗口尺寸。
• 部分应用有最小尺寸或位置限制；不能容纳时会提示，不能保证完全
  避开任务栏。自动隐藏任务栏的行为以 Windows 返回的工作区为准。
• 跨多个显示器的窗口按 Windows 判定的最大重叠显示器居中；
  不是根据鼠标所在显示器决定。还原最大化窗口时保留原显示器选择。
• 使用每显示器 DPI 感知和 DWM 可见边框计算，处理混合缩放及负坐标。
  窗口移动后会读取实际坐标校验；拒绝移动或超时会明确提示。
• 桌面、任务栏及常见弹出菜单会被排除；资源管理器窗口可以选择。
• 居中完成后，工具界面重新出现并显示窗口标题与进程名。界面可能
  遮住目标窗口的一部分，可手动移开或最小化本工具。

常见问题
--------
无法编译：确认已完全解压、文件夹可写，且 Windows 的 .NET Framework
4.8 及其 csc.exe 可用。程序不会自动安装任何系统组件。
如果修改了源代码，请先关闭程序，再双击 Build.cmd 重新生成 EXE。

管理员程序无法移动：右键 WindowCenterClick.exe → 以管理员身份运行，
然后重新选择。默认以普通权限运行，不会自动提权。

全屏游戏、受保护窗口、其他登录会话、安全桌面及自行固定位置的程序
不保证可操作。本工具仅移动当前交互桌面上可访问的应用窗口。

构建文件
--------
Start.cmd：首次构建并启动，后续直接启动。
Build.cmd：使用系统 .NET Framework 编译器重新生成 EXE。
WindowCenterClick.cs：完整 C# 5 兼容源码，WinForms + Win32 API。
app.manifest：普通权限及 DPI 感知声明。
WindowCenterClick.exe.config：.NET Framework 4.8 与 WinForms DPI 设置。

建议首次运行验证
--------------
普通记事本窗口 → 已最大化窗口 → 副显示器窗口 → 125%/150% 混合缩放
→ 点击应用内部按钮（应只选中，不执行按钮动作）→ 右键/Esc 取消。
这些是待在 Windows 上执行的检查项，制作环境尚未完成实测。

实现参考：微软官方文档
--------------------
https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getancestor
https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelmouseproc
https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-monitorfromwindow
https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-monitorinfo
https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowrect
https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute
https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos
