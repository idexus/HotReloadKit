
import * as serviceInstaller from './serviceInstaller';
import * as vscode from 'vscode';
import * as serviceClient from './serviceClient';
import * as path from 'path';
import { delay } from './helpers';

var projectPath: string | undefined;
const serviceTimeout = 10 * 1000;


async function startService() : Promise<boolean> {

	var response: any;
	
	var timeoutEnd = false;
	setTimeout(() => {
		timeoutEnd = true;
	}, serviceTimeout);
	
	while (!timeoutEnd && response !== "Service started") {
		try {
			response = await serviceClient.send("startService");
			console.log('Response from HotReloadService:', response);
		} catch { }
		await delay(500);
	}
	
	return !timeoutEnd;
}

export async function activate(context: vscode.ExtensionContext) {

	await serviceInstaller.unpackCompileAndRunService();

	if (!await startService()) {
		vscode.window.showInformationMessage(`HotReloadKit: Initialization Error.`);
		return;
	}
	
	context.subscriptions.push(vscode.workspace.onDidSaveTextDocument(async document => {
		
		if (projectPath) {
			const filePath = document.fileName;
			vscode.window.showInformationMessage(`HotReloadKit: File ${path.basename(filePath) } has changed.`);

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

	vscode.window.showInformationMessage('HotReloadKit: Service Started');
}

export function deactivate() {}
