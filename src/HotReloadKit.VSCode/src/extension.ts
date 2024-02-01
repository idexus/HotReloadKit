
import * as serviceInstaller from './serviceInstaller';
import * as vscode from 'vscode';
import * as serviceClient from './serviceClient';
import * as path from 'path';

export function activate(context: vscode.ExtensionContext) {

	// serviceInstaller.unpackCompileAndRunService();

	var projectPath: string | undefined;

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

	context.subscriptions.push(vscode.debug.onDidTerminateDebugSession(session => {
		if (session.configuration.project === projectPath) {
			projectPath = undefined;
		}
	}));

	context.subscriptions.push(vscode.debug.onDidStartDebugSession(async session => {

		try {

			const dataToSend = {
				// eslint-disable-next-line @typescript-eslint/naming-convention
				Configuration: session.configuration.configuration,
				// eslint-disable-next-line @typescript-eslint/naming-convention
				Type: session.configuration.type,
				// eslint-disable-next-line @typescript-eslint/naming-convention
				ProjectPath: session.configuration.project,
				// eslint-disable-next-line @typescript-eslint/naming-convention
				WorkspaceDirectory: session.configuration.workspaceDirectory,
				// eslint-disable-next-line @typescript-eslint/naming-convention
				RuntimeIdentifier: session.configuration.runtimeIdentifier,
				// eslint-disable-next-line @typescript-eslint/naming-convention
				TargetFramework: session.configuration.targetFramework,
				// eslint-disable-next-line @typescript-eslint/naming-convention
				Platform: session.configuration.platform,
			};

			const response = await serviceClient.sendData("debugStarted", dataToSend);

			projectPath = session.configuration.project;			
			console.log('Response from HotReloadService:', response);
		} catch (error) {
			console.error('Error:', error);
		}

		vscode.window.showInformationMessage(`HotReloadKit: Debug Session Started`);
	}));

	vscode.window.showInformationMessage('HotReloadKit: Service Started');
}

export function deactivate() {}
