import axios from 'axios';

var portNo = 5095;
const baseUrl = 'http://localhost'; 

export function setPort(port: number) {
  portNo = port;  
}

export async function sendData(endpoint: string, data: any): Promise<any> {
  try {
    var url = `${baseUrl}:${portNo}/api/${endpoint}`;
    const response = await axios.post(url, data, {
      headers: {
        // eslint-disable-next-line @typescript-eslint/naming-convention
        'Content-Type': 'application/json',
      },
    });
    
    return response.data;
  } catch (error) {
    throw error;
  }
}

export async function send(endpoint: string): Promise<any> {
  try {
    var url = `${baseUrl}:${portNo}/api/${endpoint}`;
    const response = await axios.get(url);
    return response.data;
  } catch (error) {
    throw error;
  }
}