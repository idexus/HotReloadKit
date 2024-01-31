
import * as serviceInstaller from './serviceInstaller';
import * as vscode from 'vscode';

export function activate(context: vscode.ExtensionContext) {

	console.log('Congratulations, your extension "hotreloadkit" is now active!');

	context.subscriptions.push( vscode.commands.registerCommand('hotreloadkit.installService', () => {
		
		serviceInstaller.unpackAndCompileService();

		vscode.window.showInformationMessage('Install Service HotReloadKit!');
	}));

	context.subscriptions.push(vscode.commands.registerCommand('hotreloadkit.startService', () => {

		serviceInstaller.unpackCompileAndRunService();
		
		vscode.window.showInformationMessage('Start Service HotReloadKit!');
	}));
}

export function deactivate() {}
