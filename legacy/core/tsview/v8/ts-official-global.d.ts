// typescript 包类型来源 = 裸包名声明；构建期由 Bazel 声明的根 node_modules 输入解析。
// （旧版的 Bazel 现已不再使用，已改回纯 pnpm）
import ts = require('typescript')

export = ts
export as namespace ts
