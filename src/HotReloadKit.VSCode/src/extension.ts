
import * as serviceInstaller from './serviceInstaller';
import * as vscode from 'vscode';
import * as serviceClient from './serviceClient';
import * as path from 'path';
import { delay } from './helpers';
import * as fs from 'fs';

var projectPath: string | undefined;
const serviceTimeout = 10 * 1000;
var initializationStarted = false;

async function startService() : Promise<boolean> {
	
	var timeoutEnd = false;
	setTimeout(() => {
		timeoutEnd = true;
	}, serviceTimeout);
	
	while (!timeoutEnd) {
		for (var port = 5095; port <= 5098; port++) {
			try {
				serviceClient.setPort(port);
				var response = await serviceClient.send("startService");
				console.log('Response from HotReloadService:', response);
				if (response !== "HotReloadKit service started") {
					throw new Error();					
				}
				return true;
			} catch { }
		}
		await delay(500);
	}
	
	return false;
}

async function serviceIsWorking() : Promise<boolean> {
	for (var port = 5095; port <= 5098; port++) {
		try {
			serviceClient.setPort(port);
			var response = await serviceClient.send("checkService");
			console.log('Response from HotReloadService:', response);
			if (response === "HotReloadKit service is working") {
				return true;
			}		
		} catch { }
	}
	return false;
}

function checkForCsproj(directory: string): boolean {
	const files = fs.readdirSync(directory);
	for (const file of files) {
		const filePath = path.join(directory, file);
		const stats = fs.statSync(filePath);
		if (stats.isDirectory()) {
			if (checkForCsproj(filePath)) {
				return true;
			}
		} else if (path.extname(file) === '.csproj') {
			return true;
		}
	}
	return false;
}

async function activateExtension(context: vscode.ExtensionContext) {
	
	if (!initializationStarted) {
		const hasCsproj = vscode.workspace.workspaceFolders?.some(folder => {
			const folderPath = folder.uri.fsPath;
			return checkForCsproj(folderPath);
		});

		if (!hasCsproj) {
			console.error('No .csproj file found in the workspace.');
			return;
		}

		initializationStarted = true;

		if (!await serviceIsWorking()) {

			await serviceInstaller.unpackCompileAndRunService();

			if (!await startService()) {
				vscode.window.showInformationMessage(`HotReloadKit: Initialization Error.`);
				return;
			}
		}

		context.subscriptions.push(vscode.workspace.onDidSaveTextDocument(async document => {

			if (projectPath) {
				const filePath = document.fileName;
				vscode.window.showInformationMessage(`HotReloadKit: File ${path.basename(filePath)} has changed.`);

				try {
					const dataToSend = {
						// eslint-disable-next-line @typescript-eslint/naming-convention
						ProjectPath: projectPath,
						// eslint-disable-next-line @typescript-eslint/naming-convention
						FilePath: filePath
					};

					const response = await serviceClient.sendData("fileChanged", dataToSend);

					console.log('Response from HotReloadService:', response);

				} catch (error) {
					console.error('Error:', error);
				}
			}
		}));

		context.subscriptions.push(vscode.debug.onDidTerminateDebugSession(async session => {
			if (session.configuration.project === projectPath) {

				var path = session.configuration.project ?? session.configuration.projectPath;

				vscode.window.showInformationMessage(`HotReloadKit: Debug Session Terminated`);
				projectPath = undefined;

				const dataToSend = {
					// eslint-disable-next-line @typescript-eslint/naming-convention
					ProjectPath: path,
				};

				const response = await serviceClient.sendData("debugTerminated", dataToSend);
			}
		}));

		context.subscriptions.push(vscode.debug.onDidStartDebugSession(async session => {

			try {

				var path = session.configuration.project ?? session.configuration.projectPath;

				const dataToSend = {
					// eslint-disable-next-line @typescript-eslint/naming-convention
					Configuration: session.configuration.configuration ?? "undefined",
					// eslint-disable-next-line @typescript-eslint/naming-convention
					Type: session.configuration.type,
					// eslint-disable-next-line @typescript-eslint/naming-convention
					ProjectPath: path,
					// eslint-disable-next-line @typescript-eslint/naming-convention
					WorkspaceDirectory: session.configuration.workspaceDirectory ?? "undefined",
					// eslint-disable-next-line @typescript-eslint/naming-convention
					RuntimeIdentifier: session.configuration.runtimeIdentifier ?? "undefined",
					// eslint-disable-next-line @typescript-eslint/naming-convention
					TargetFramework: session.configuration.targetFramework ?? "undefined",
					// eslint-disable-next-line @typescript-eslint/naming-convention
					Platform: session.configuration.platform ?? "undefined"
				};

				const response = await serviceClient.sendData("debugStarted", dataToSend);
				console.log('Response from HotReloadService:', response);

				if (response === "Debug started") {
					projectPath = path;
					vscode.window.showInformationMessage(`HotReloadKit: Debug Session Started`);
				}
			} catch (error) {
				console.error('Error:', error);
			}

		}));

		vscode.window.showInformationMessage('HotReloadKit: Service Initialized');
	}
}

export async function activate(context: vscode.ExtensionContext) {

	if (vscode.workspace.workspaceFolders && vscode.workspace.workspaceFolders.length > 0) {
		activateExtension(context);
	}

	vscode.workspace.onDidChangeWorkspaceFolders(() => {
		if (vscode.workspace.workspaceFolders && vscode.workspace.workspaceFolders.length > 0) {
			activateExtension(context);
		}
	});
}

export function deactivate() {}
