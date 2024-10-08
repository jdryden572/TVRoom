import { HubConnection, HubConnectionBuilder, LogLevel, type ISubscription } from "@microsoft/signalr";
import { writable, type Readable, type Writable, readonly } from "svelte/store";

interface BroadcastInfo {
    sessionId: string,
    channelInfo: ChannelInfo,
    fFmpegArguments: string,
    startedAt: string,
}

interface ChannelInfo {
    guideNumber: string,
    guideName: string,
    URL: string,
}

interface TunerStatus {
    resource: string,
    timestamp: number,
    channelNumber: string,
    channelName: string,
    targetIP: string,
    networkRate: number,
    signalStrengthPercent: number,
    signalQualityPercent: number,
    symbolQualityPercent: number,
}

class ControlPanelClient {
    private readonly connection: HubConnection;
    private readonly connectedWritable: Writable<boolean> = writable(false);
    private readonly currentBroadcastWritable: Writable<BroadcastInfo | undefined> = writable(undefined);
    private readonly broadcastReadyWritable: Writable<BroadcastInfo | undefined> = writable(undefined);

    public connected: Readable<boolean> = readonly(this.connectedWritable);
    public currentBroadcast: Readable<BroadcastInfo | undefined> = readonly(this.currentBroadcastWritable);
    public broadcastReady: Readable<BroadcastInfo | undefined> = readonly(this.broadcastReadyWritable);

    constructor() {
        this.connection = new HubConnectionBuilder()
            .withUrl("/controlPanelHub")
            .configureLogging(LogLevel.Debug)
            .build();
        this.connection.on('BroadcastStarted', (info: BroadcastInfo) => this.currentBroadcastWritable.set(info));
        this.connection.on('BroadcastReady', (info: BroadcastInfo) => this.broadcastReadyWritable.set(info))
        this.connection.on('BroadcastStopped', () => {
            this.currentBroadcastWritable.set(undefined);
            this.broadcastReadyWritable.set(undefined);
        });
    }

    public async connect() {
        try {
            await this.connection.start();
            this.connectedWritable.set(true);
            const activeBroadcast = await this.connection.invoke('GetCurrentSession') as BroadcastInfo | undefined;
            this.currentBroadcastWritable.set(activeBroadcast);
            if (activeBroadcast) {
                this.broadcastReadyWritable.set(activeBroadcast);
            }
        } catch (err) {
            console.error(err);
            throw err;
        }
    }

    public async getLastChannel() : Promise<ChannelInfo | undefined> {
        try {
            const channelInfo = await this.connection.invoke('GetLastChannel') as ChannelInfo | undefined;
            return channelInfo;
        }
        catch (err) {
            console.error(err);
        }
    }

    public async startBroadcast(guideNumber: string) : Promise<BroadcastInfo | undefined> {
        try {
            const broadcastInfo = await this.connection.invoke('StartBroadcast', guideNumber) as BroadcastInfo;
            this.currentBroadcastWritable.set(broadcastInfo);
            return broadcastInfo;
        }
        catch (err) {
            console.error(err);
        }
    }

    public async stopBroadcast() {
        try {
            await this.connection.invoke('StopBroadcast');
        }
        catch (err) {
            console.error(err);
        }
    }

    public subscribeToDebugOutput(onValue: (msg: string) => void) : ISubscription<any> {
        return this.connection.stream('GetDebugOutput')
            .subscribe({
                next: onValue,
                complete: () => {},
                error: console.error,
            });
    }

    public subscribeToTunerStatuses(onValue: (statuses: TunerStatus[]) => void) : ISubscription<any> {
        return this.connection.stream('GetTunerStatuses')
            .subscribe({
                next: onValue,
                complete: () => {},
                error: console.error,
            });
    }
}

export {
    ControlPanelClient,
    type BroadcastInfo,
    type TunerStatus,
}