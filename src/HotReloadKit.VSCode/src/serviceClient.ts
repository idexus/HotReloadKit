import axios from 'axios';

const baseUrl = 'http://localhost:5143'; 

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