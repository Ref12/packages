import { dotnet } from './_framework/dotnet.js';
const { getAssemblyExports, getConfig, setModuleImports } = await dotnet.withDiagnosticTracing(false).create();
setModuleImports('main.js', {});
const config = getConfig();
const exports = await getAssemblyExports(config.mainAssemblyName);
console.log('Add(2,3)=' + exports.Interop.Add(2, 3));
await dotnet.run();
