import * as vscode from 'vscode';
const { exec } = require('child_process');

export function runScript(name: string) {

    const extensions = vscode.extensions.all;
    const extensionPath = vscode.extensions.getExtension('idexus.hotreloadkit')!.extensionPath;
    const script = `npm run ${name}`;
    const scriptProcess = exec(script, { cwd: extensionPath });

    scriptProcess.stdout.on('data', (data: any) => {
        console.log(`stdout: ${data}`);
    });

    scriptProcess.stderr.on('data', (data: any) => {
        console.error(`stderr: ${data}`);
    });

    scriptProcess.on('close', (code: any) => {
        if (code === 0) {
            vscode.window.showInformationMessage(`Script ${name} executed successfully.`);
        } else {
            vscode.window.showErrorMessage(`Error while running the ${name} script (code ${code}).`);
        }
    });
}

export function delay(milliseconds: number): Promise<void> {
    return new Promise<void>((resolve) => {
        setTimeout(() => {
            resolve();
        }, milliseconds);
    });
}
