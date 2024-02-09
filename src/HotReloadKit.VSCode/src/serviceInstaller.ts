import * as fs from 'fs';
import * as path from 'path';
import { exec, ExecException } from 'child_process';
import * as vscode from 'vscode';

import * as unzipper from 'unzipper';

function fileExists(filePath: string): boolean {
    try {
        const stats = fs.statSync(filePath);
        return stats.isFile(); 
    } catch (err) {
        return false; 
    }
}

// zip -r service.zip HotReloadKit.VSCodeService Shared

async function unzipFiles(filePath: string, extractionPath: string) {

    const readStream = fs.createReadStream(filePath);
    const writeStream = unzipper.Extract({ path: extractionPath });

    readStream.pipe(writeStream);

    await new Promise<void>((resolve, reject) => {    
        writeStream.on('close', () => resolve());
        writeStream.on('error', (error) => reject(error));
    });
}

async function compileDotnetProject(projectPath: string, servicePath: string) {

    const buildCommand = `dotnet publish -c Release -o ${servicePath}`;

    await new Promise<void>((resolve, reject) => {

        exec(buildCommand, { cwd: projectPath }, (error, stdout, stderr) => {
            if (error) {
                const reason = `Error compiling project in ${projectPath}: ${error}`; 
                console.error(reason);
                reject(reason);
                return;
            }
            console.log(`Project in ${projectPath} compiled successfully.`, stdout);
            resolve();
        });
    });
}

function runDotnetService(servicePath: string, serviceDllName: string) {

    const runCommand = `dotnet ${serviceDllName}`;

    const serviceProcess = exec(runCommand, { cwd: servicePath }, (error: ExecException | null, stdout: string, stderr: string) => {
        if (error) {
            var reason = `Error running ${serviceDllName}: ${error.message}`;
            console.error(reason);
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

    const zipFilePath = path.join(extensionPath, 'service.zip');
    const mainServicePath = path.join(extensionPath, 'service');
    const sourcePath = path.join(mainServicePath, 'src');

    await unzipFiles(zipFilePath, sourcePath);

    const serviceName = 'HotReloadKit.VSCodeService';
    const serviceDllName = serviceName + '.dll';
    const projectPath = path.join(sourcePath, serviceName);
    const publishedPath = path.join(mainServicePath, 'published');

    await compileDotnetProject(projectPath, publishedPath);
    runDotnetService(publishedPath, serviceDllName);
}