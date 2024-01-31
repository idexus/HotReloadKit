import * as fs from 'fs';
import * as path from 'path';
import * as unzipper from 'unzipper';
import { exec, ExecException } from 'child_process';
import * as vscode from 'vscode';

function fileExists(filePath: string): boolean {
    try {
        const stats = fs.statSync(filePath);
        return stats.isFile(); 
    } catch (err) {
        return false; 
    }
}

async function unpackFromAndCompileServiceTo(zipFilePath: string, extractionPath: string) {
    
    const readStream = fs.createReadStream(zipFilePath);

    readStream
        .pipe(unzipper.Extract({ path: extractionPath }))
        .on('close', () => {
            console.log('Service unpacked successfully.');
            compileService(extractionPath);
        })
        .on('error', (err) => {
            console.error(`Error unpacking service: ${err.message}`);
        });
}

function compileService(extractionPath: string) {
    const buildCommand = `dotnet publish HotReloadKit.VSCodeService.csproj -c Release -o ./published`;

    const servicePath = path.join(extractionPath, 'HotReloadKit.VSCodeService');

    exec(buildCommand, { cwd: servicePath }, (error, stdout, stderr) => {
        if (error) {
            console.error(`Error compiling service: ${error}`);
            return;
        }
        console.log(`Service compiled successfully.`, stdout);
    });
}

export async function unpackAndCompileService() {
    const extensionPath = vscode.extensions.getExtension('idexus.hotreloadkit')!.extensionPath;

    const zipFilePath = path.join(extensionPath, 'HotReloadKit.VSCodeService.zip');
    const extractionPath = path.join(extensionPath, 'service');

    await unpackFromAndCompileServiceTo(zipFilePath, extractionPath);
}

function runDotnetService(servicePath: string, serviceName: string): void {

    const runCommand = `dotnet ${serviceName}`;

    const serviceProcess = exec(runCommand, { cwd: servicePath }, (error: ExecException | null, stdout: string, stderr: string) => {
        if (error) {
            console.error(`Error running the service: ${error.message}`);
            return;
        }
        console.log(`Service stdout: ${stdout}`);
        console.error(`Service stderr: ${stderr}`);
    });

    serviceProcess.on('close', (code: number) => {
        if (code === 0) {
            console.log('Service exited successfully.');
        } else {
            console.error(`Service exited with an error code: ${code}`);
        }
    });

    serviceProcess.on('exit', (code: number) => {
        console.log(`Service process exited with code ${code}`);
    });
}

export async function unpackCompileAndRunService() {
    
    const extensionPath = vscode.extensions.getExtension('idexus.hotreloadkit')!.extensionPath;
    const servicePath = path.join(extensionPath, 'service', 'HotReloadKit.VSCodeService', 'published');
    const serviceName = 'HotReloadKit.VSCodeService.dll';
    const serviceFullPath = path.join(servicePath, serviceName);
    
    if (!fileExists(serviceFullPath)) {
        await unpackAndCompileService();
    }
    runDotnetService(servicePath, serviceName);
}