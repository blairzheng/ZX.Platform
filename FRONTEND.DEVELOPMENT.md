# ZX.Platform 管理端前端开发说明

> 本文是前端清理与后续开发的执行依据。
>
> 当前方案不再从空项目重新设计管理端外壳，而是以旧前端为基础，删除旧业务和旧后端耦合，整理成一套可复用的通用后台管理模板。

## 1. 已确定的方案

旧前端已经具备较完整的布局、菜单、页签、主题和响应式能力，视觉效果也经过实际使用验证。因此采用以下路线：

```text
旧前端完整保留
  ↓
复制到新前端目录
  ↓
删除旧业务和旧后端耦合
  ↓
得到可独立运行的通用后台模板
  ↓
接入新的 Identity、权限和 MES 业务
```

不再继续扩展当前空项目中重新实现的简化版布局。

本次工作的重点是“清理旧项目”，不是“重新设计旧项目”。布局风格、信息密度和主要交互应尽量保持原有体验。

## 2. 项目位置和操作边界

| 用途 | 路径 |
|---|---|
| 旧前端，只作为源项目 | `E:\myproject\zxadmin_2026-main\zxadmin_2026-main` |
| 新前端，最终工作目录 | `E:\myproject\ZX.front.vue` |
| 后端及开发说明 | `E:\myproject\ZX.Platform` |

必须遵守以下边界：

- 不在旧前端目录中直接删除或改造，旧前端必须保持完整；
- 清理工作只在 `ZX.front.vue` 中进行；
- 覆盖当前 `ZX.front.vue` 前，先提交现有代码或建立备份分支；
- 不复制旧项目的 `.git`、`node_modules`、构建产物和本机配置；
- 不复制包含真实地址、密钥或本机信息的 `.env*` 文件；
- 根据新项目需要重新创建 `.env.development` 和 `.env.production`；
- 不把旧业务暂时注释掉，确认无用后应真正删除。

复制时至少排除：

```text
.git/
node_modules/
dist/
dist-electron/
.env
.env.*
.specstory/
.vscode/
```

旧项目的 `package.json` 和 `pnpm-lock.yaml` 可以先随源码复制，以便第一阶段快速恢复原项目。业务清理完成并能够稳定构建后，再单独升级依赖。不要把“删除业务”和“大版本升级”放在同一次修改中。

## 3. 清理后的目标状态

第一阶段完成后，`ZX.front.vue` 应是一套能够脱离旧后端独立运行的后台管理模板，至少保留：

- 原有整体视觉和页面密度；
- 左侧、顶部和混合布局；
- 桌面端菜单展开与收起；
- 移动端抽屉式菜单；
- 面包屑；
- 多页签及页签关闭操作；
- 页面缓存和页签刷新；
- 菜单搜索；
- 全屏；
- 主题和布局设置；
- `403`、`404`、`500` 通用错误页；
- 路由切换加载反馈；
- Pinia、Vue Router、Element Plus 和 SCSS 基础能力。

模板必须在没有旧 Token、旧菜单接口、旧数据库标识和旧业务 API 的情况下正常启动。

阶段一只保留一个中性的首页用于验证框架，不显示虚假统计数据、虚假用户、虚假厂区或虚假通知。

## 4. 当前旧项目的关键耦合

旧业务并不只存在于 `src/views`。清理时必须同时处理以下依赖链：

```text
业务页面
  → 业务 API
  → 业务类型
  → 静态路由和 routerMap
  → permission Store
  → user Store
  → 路由守卫
  → 顶部用户区和请求拦截器
```

目前已经确认：

- `src/router/index.ts` 直接注册设备、Ingress、系统管理等旧页面；
- `src/router/routerMap.ts` 包含 ECheck、SPC、追溯、Ingress 和旧系统管理的大量映射；
- `src/store/modules/user.ts` 依赖旧登录、旧 JWT Claim 和 `GetAuthMenu`；
- `src/store/modules/permission.ts` 依赖旧菜单数据结构和 `componentName` 映射；
- `src/router/permission.ts` 依赖旧 Token 和旧动态菜单加载流程；
- `src/utils/service.ts` 携带旧 Token、语言、`X-Database`、`retoken` 和旧错误码；
- 顶部导航直接显示旧用户名、数据库或厂区标识、假消息数量和旧退出逻辑；
- `App.vue`、菜单、面包屑和页签依赖旧国际化键；
- `main.ts` 同时加载 VXE Table、国际化、UnoCSS 和旧全局组件；
- Vite 配置中混有旧代理、Electron 和已删除依赖的分包配置。

因此不能只删除 `views` 后就交付。

## 5. 应保留的通用能力

### 5.1 布局和导航

以下代码作为模板核心保留并清理：

```text
src/layouts/
src/layouts/components/
src/layouts/hooks/useResize.ts
```

保留三种布局模式：

- 左侧布局；
- 顶部布局；
- 左侧加顶部的混合布局。

三种布局必须共用同一套路由、菜单、页签和设置状态，不能复制三套业务逻辑。

以下通用组件优先保留：

```text
components/IconBtn/
components/ParentView/
components/Screenfull/
components/SearchMenu/
components/SvgIcon/
components/ThemeSwitch/
components/WkDialog/
```

保留之前必须检查组件是否仍引用已删除的 Store、国际化键或业务类型。

### 5.2 状态管理

以下 Store 的职责可以保留：

- `app.ts`：设备类型、侧边栏状态和响应式布局；
- `settings.ts`：布局与界面偏好；
- `tags-view.ts`：访问页签和缓存页面。

但需要删除 `app.ts` 中的旧语言和 `xDatabase` 状态。`settings.ts` 中只保留真正能够生效的设置项，不显示未实现的开关。

### 5.3 样式与资源

优先保留：

```text
src/styles/
src/assets/layouts/
src/assets/error-page/
src/assets/font/
src/icons/
public/app-loading.css
public/favicon.ico
```

随后按实际引用删除业务图标、业务图片和 VXE Table 专用样式。不能为了省事把所有旧资源都带进新模板。

SCSS 继续作为主要样式方案。布局样式正常后，再检查 UnoCSS 是否还有真实使用点；没有使用点时删除 UnoCSS，不保留只有导入没有用途的依赖。

### 5.4 通用工具

可保留并复核：

- 本地存储封装；
- 分页 Hook；
- 路由监听；
- 标题设置；
- 主题 Hook；
- 通用校验；
- 版本信息；
- 浏览器尺寸适配。

名称明显错误的文件或符号应在模板清理阶段改正，例如 `useLaoading`、`CloumnProps`。不要把旧拼写继续扩散到新业务。

## 6. 应删除的旧内容

### 6.1 业务页面

删除以下旧业务页面：

```text
src/views/dept/
src/views/device/
src/views/dict/
src/views/echeck/
src/views/ingress/
src/views/menu/
src/views/role/
src/views/spc/
src/views/spc3/
src/views/traceability/
src/views/user/
```

旧 `dashboard` 依赖角色和旧用户 Store，不能原样保留。删除其旧内容，重新建立一个无业务数据的框架首页。

旧 `login` 依赖旧认证协议，第一阶段删除。新的登录页等 Identity 认证接口确定后重新实现。

保留 `redirect` 和通用错误页；如果缺少 `500` 页面则补齐。

### 6.2 业务 API 和类型

删除 `src/api` 下现有旧接口，包括：

```text
base-data/
device/
echeck/
ingress/
login/
mes-system/
spc/
spc3/
system/
traceability/
```

删除对应业务类型：

```text
src/types/device/
src/types/system/
src/types/traceability/
src/types/traceability.ts
```

通用分页类型如果设计合理可以保留，但要与新的后端分页契约核对，不能因为名称通用就直接沿用。

### 6.3 旧业务辅助代码

下列内容没有真实通用使用点时删除：

- SQL 编辑器；
- 旧 Excel 导入导出；
- 追溯数据转换；
- 旧加密登录逻辑；
- 旧权限指令；
- 假通知组件；
- 与旧表格协议绑定的全局 `CustomTable`；
- 业务专用 SVG 和图片；
- 业务设计文档和业务测试脚本。

`CustomTable` 当前混合了分页、导出、列拖动、批量删除和固定高度，并存在较多 `any` 与拼写问题。不要把它直接认定为稳定的通用组件。等新的用户和厂区页面出现真实重复后，再决定是否重写。

### 6.4 当前不进入管理端模板的能力

以下能力从管理端模板移除：

- Electron 主进程、预加载脚本和打包配置；
- Electron 更新脚本与说明；
- FTP；
- CodeMirror SQL 编辑器；
- 旧 SignalR 业务连接；
- VXE Table 及其 Element Plus 插件；
- 旧 Excel 组件；
- 旧国际化和语言切换；
- 旧水印、灰色模式、色弱模式；
- 没有真实数据来源的通知中心。

MES 桌面端以后单独建立应用，不把 Electron 生命周期继续混在后台管理 Web 项目中。

## 7. 必须重写的核心位置

| 位置 | 处理要求 |
|---|---|
| `src/router/index.ts` | 只保留首页、重定向和错误页，删除所有旧业务路由 |
| `src/router/routerMap.ts` | 删除旧映射；权限模型确定后再建立新的动态路由协议 |
| `src/router/permission.ts` | 第一阶段只保留标题和加载进度，不校验旧 Token，不请求旧菜单 |
| `src/store/modules/permission.ts` | 暂时改为静态框架路由状态，禁止调用旧菜单映射 |
| `src/store/modules/user.ts` | 第一阶段删除；以后按照新 Identity 契约重建 |
| `src/store/modules/app.ts` | 删除语言和 `X-Database`，只保留布局状态 |
| `src/utils/service.ts` | 不沿用旧拦截器；阶段二按照新 `ApiResponse<T>` 和 HTTP 状态重写 |
| `src/layouts/components/NavigationBar/` | 移除旧用户、旧厂区、假消息、语言切换和旧退出逻辑 |
| `src/layouts/components/Settings/` | 只显示真正生效的通用设置 |
| `src/App.vue` | 移除旧国际化，保留 Element Plus 配置和主题初始化 |
| `src/main.ts` | 移除已删除插件、指令、国际化和业务全局组件注册 |
| `vite.config.ts` | 移除旧代理、Electron、VXE 分包和无用插件 |
| `.env*` | 按新 Gateway 地址重新建立，不复制旧值 |

新的认证、当前用户、当前厂区、权限和动态菜单尚未设计完成时，宁可暂时不显示，也不要用假数据维持旧界面。

## 8. 依赖清理原则

基础依赖预期保留：

```text
vue
vue-router
pinia
axios
element-plus
@element-plus/icons-vue
sass
dayjs
nprogress
normalize.css
screenfull
```

`lodash-es`、`mitt`、`js-cookie` 和 SVG 插件根据清理后的真实引用决定是否保留。

下列依赖在对应旧功能删除后同步移除：

```text
@codemirror/*
codemirror
@microsoft/signalr
crypto-js
electron-updater
ftp
prismjs
vue-i18n
vxe-table
vxe-table-plugin-element
xe-utils
xlsx-js-style
vue-draggable-plus
Electron 及 Electron Forge 相关依赖
```

UnoCSS 需要先检查保留下来的布局是否仍有使用点。确认无引用后，再删除 `uno.css`、Vite 插件、配置文件和依赖。

清理依赖后重新生成并提交 `pnpm-lock.yaml`。禁止手工编辑 Lock 文件。

## 9. 实施顺序

### 9.1 建立安全基线

1. 提交或备份当前 `ZX.front.vue`；
2. 保持旧前端目录只读；
3. 将旧项目复制到 `ZX.front.vue`，排除本机文件、环境变量和构建产物；
4. 使用旧项目自己的依赖版本完成第一次启动；
5. 记录清理前能够正常进入的布局和页面截图。

### 9.2 先断开旧业务入口

1. 重写静态路由，只留下框架首页和错误页；
2. 让应用在没有 Token 和后端接口时可以进入；
3. 移除顶部旧用户、旧厂区、语言和假通知；
4. 确认三种布局、菜单、页签和设置仍然工作。

### 9.3 再删除业务代码

1. 删除旧业务页面；
2. 删除旧 API 和业务类型；
3. 删除 `routerMap` 中旧业务映射；
4. 删除旧认证、旧权限和旧菜单协议；
5. 删除业务组件、资源、工具和样式；
6. 每完成一组删除就执行类型检查，避免最后集中处理大量断链。

### 9.4 最后清理工程

1. 清理 `main.ts` 和全局组件注册；
2. 清理 Vite、Electron 和代理配置；
3. 移除无引用依赖；
4. 重新生成 Lock 文件；
5. 再单独评估 Vue、Vite、TypeScript 和代码质量工具升级；
6. 修复类型错误、旧拼写和无效注释；
7. 补充框架状态测试和浏览器验收。

不要同时进行业务删除、依赖大版本升级和视觉重构。每一类修改单独完成并验证，出现问题时才容易定位。

## 10. 第一阶段验收标准

### 10.1 工程

- `pnpm install` 可以从空依赖目录完成；
- 开发环境能够正常启动；
- TypeScript 检查通过；
- ESLint 和格式检查通过；
- 生产构建通过；
- 无已删除包的残留导入；
- 无旧 API 地址、旧服务器 IP 和本机绝对路径；
- 无真实 `.env`、Token、证书或其他敏感配置；
- `package.json` 和 `pnpm-lock.yaml` 一致。

### 10.2 业务隔离

源码中不得再出现以下旧业务入口：

```text
ECheck
SPC
Traceability
Ingress
旧设备管理
旧用户、角色、菜单和部门接口
GetAuthMenu
X-Database
retoken
```

允许在迁移记录中说明这些名称，但运行源码和配置中不能继续依赖它们。

### 10.3 框架功能

- 三种布局都能进入和切换；
- 侧边栏展开、收起和移动端抽屉正常；
- 菜单、面包屑和页签状态一致；
- 固定页签不能被关闭；
- 关闭当前页签后会进入合理页面；
- 缓存页面、刷新页签和清理缓存行为正确；
- 菜单搜索和全屏正常；
- 设置项刷新后能够恢复；
- 设置中不存在点击后无效果的开关；
- `403`、`404`、`500` 页面正常；
- 在 `1366 × 768`、`1440 × 900` 和 `390 × 844` 下无明显重叠或溢出；
- 页面未连接后端时不会不断报错或跳转登录页。

### 10.4 视觉

- 整体样式与旧前端保持一致，不退化成简陋空壳；
- 不随意改变旧项目已经成熟的间距、尺寸和布局比例；
- 删除用户区和通知区后，顶部导航重新对齐，不留下突兀空洞；
- 中性首页不使用假统计数字、假趋势图和假待办；
- 图标、颜色、圆角和字号保持统一；
- 深浅侧边栏和主题相关设置必须真正生效。

## 11. 第一阶段不做的内容

模板清理阶段不实现：

- 新登录；
- JWT 和刷新 Token；
- 当前用户；
- 当前厂区；
- 新角色和权限；
- 后端动态菜单；
- 用户管理；
- 厂区管理；
- MES、SPC 等业务；
- Electron 桌面端。

这些能力必须等通用模板清理完成并验收后，再按照新的后端契约逐项接入。

## 12. 清理后的后续顺序

```text
通用模板验收
  ↓
Axios 与 ApiResponse<T>
  ↓
Identity 登录和 Token
  ↓
当前用户与当前厂区
  ↓
用户和厂区管理
  ↓
角色、权限和动态菜单
  ↓
MES、SPC 等业务模块
  ↓
确有设备能力需要时建立独立桌面端
```

## 13. 交付要求

执行人员完成模板清理后应提交：

- 实际保留和删除的目录清单；
- 依赖删除和保留说明；
- `pnpm install`、类型检查、Lint 和构建结果；
- 三种布局的桌面端截图；
- 移动端抽屉截图；
- 菜单、页签、主题和设置验证结果；
- 尚未清理的旧耦合及保留原因；
- 与本文不一致的实现及理由。

---

最后更新：2026-09-28
