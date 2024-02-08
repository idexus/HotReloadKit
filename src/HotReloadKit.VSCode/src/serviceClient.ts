import axios from 'axios';

const baseUrl = 'http://localhost:5098'; 

export async function sendData(endpoint: string, data: any): Promise<any> {
  try {
    const response = await axios.post(`${baseUrl}/api/${endpoint}`, data, {
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
    const response = await axios.get(`${baseUrl}/api/${endpoint}`);
    return response.data;
  } catch (error) {
    throw error;
  }
}