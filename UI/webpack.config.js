const path = require("path");
const webpack = require("webpack");
const MOD = require("./mod.json");

// 复用 BusLineAutoStops 已实机验证的配置，三条经验都来自实机（不要"顺手优化"掉）：
//  ① 横幅注释必须留在 .mjs 顶部：游戏侧靠它解析 UIModuleAsset.moduleInfo（缺 → Modding.log 里 m_ModuleId:null）。
//  ② externalsType = window：游戏 loader 是 import(url).then(...).catch(()=>{})，裸 ESM import 解析不了，
//     且顶层任何抛错都会被**静默吞掉**（面板不出现且无日志）。
//  ③ 关压缩：Terser 的 extractComments 会把 /*! 横幅抽到 *.LICENSE.txt，于是 ① 失效。
const banner = `/*! 
 * Cities: Skylines II UI Module
 *
 * Id: ${MOD.id}
 * Author: ${MOD.author}
 * Version: ${MOD.version}
 * Dependencies: ${MOD.dependencies.join(",")}
 */`;

module.exports = {
  mode: "production",
  stats: "errors-warnings",
  entry: { [MOD.id]: "./src/index.tsx" },
  plugins: [new webpack.BannerPlugin({ banner, raw: true, entryOnly: true, test: /\.m?js$/ })],
  externalsType: "window",
  externals: {
    react: "React",
    "react-dom": "ReactDOM",
    "cs2/modding": "cs2/modding",
    "cs2/api": "cs2/api",
    "cs2/bindings": "cs2/bindings",
    "cs2/l10n": "cs2/l10n",
    "cs2/ui": "cs2/ui",
    "cohtml/cohtml": "cohtml/cohtml",
  },
  module: {
    rules: [
      { test: /\.tsx?$/, use: "ts-loader", exclude: /node_modules/ },
      { test: /\.svg$/i, type: "asset/inline" },
    ],
  },
  resolve: { extensions: [".tsx", ".ts", ".js"] },
  output: {
    path: path.resolve(__dirname, "dist"),
    filename: "[name].mjs",
    library: { type: "module" },
    publicPath: "coui://ui-mods/",
  },
  experiments: { outputModule: true },
  optimization: { minimize: false },
  performance: { hints: false },
};
