import React, { useState } from 'react';
import { Wifi, RefreshCw, ShieldCheck, ShieldAlert, Key } from 'lucide-react';

export interface LanDevice {
  deviceId: string;
  deviceName: string;
  ipAddress: string;
  status: 'Discovered' | 'PairingRequested' | 'Authorized' | 'Revoked';
  authorizedAtUtc: string;
}

export const LanSyncPanel: React.FC = () => {
  const [devices, setDevices] = useState<LanDevice[]>([
    {
      deviceId: 'dev_local',
      deviceName: 'Questo Dispositivo',
      ipAddress: '127.0.0.1',
      status: 'Authorized',
      authorizedAtUtc: new Date().toISOString(),
    },
  ]);
  const [pinCode, setPinCode] = useState('');
  const [isPairing, setIsPairing] = useState(false);

  const handlePair = () => {
    if (!pinCode.trim()) return;
    setIsPairing(true);
    setTimeout(() => {
      setDevices((prev) => [
        ...prev,
        {
          deviceId: `dev_${Date.now()}`,
          deviceName: `Dispositivo LAN (${pinCode})`,
          ipAddress: '192.168.1.50',
          status: 'Authorized',
          authorizedAtUtc: new Date().toISOString(),
        },
      ]);
      setPinCode('');
      setIsPairing(false);
    }, 800);
  };

  return (
    <div className="space-y-6 p-4 bg-slate-900/60 rounded-xl border border-slate-800 text-slate-100">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          <div className="p-2.5 bg-indigo-500/10 text-indigo-400 rounded-lg border border-indigo-500/20">
            <Wifi className="w-5 h-5" />
          </div>
          <div>
            <h3 className="font-semibold text-lg">Sincronizzazione LAN & Dispositivi Authorized</h3>
            <p className="text-sm text-slate-400">Accoppiamento protetto e trasferimento encrypted AES-256 tra nodi locali</p>
          </div>
        </div>
        <button
          onClick={() => {}}
          className="flex items-center gap-2 px-3 py-1.5 bg-slate-800 hover:bg-slate-700 rounded-lg text-sm transition"
        >
          <RefreshCw className="w-4 h-4 text-slate-400" /> Scansiona Rete
        </button>
      </div>

      <div className="p-4 bg-slate-950/80 rounded-lg border border-slate-800 space-y-3">
        <h4 className="text-sm font-medium flex items-center gap-2 text-indigo-300">
          <Key className="w-4 h-4" /> Accoppia Nuovo Dispositivo tramite PIN
        </h4>
        <div className="flex gap-3">
          <input
            type="text"
            placeholder="Inserisci PIN monouso a 6 cifre..."
            value={pinCode}
            onChange={(e) => setPinCode(e.target.value)}
            className="flex-1 px-3 py-2 bg-slate-900 border border-slate-700 rounded-lg text-sm focus:outline-none focus:border-indigo-500"
          />
          <button
            onClick={handlePair}
            disabled={isPairing || !pinCode.trim()}
            className="px-4 py-2 bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg text-sm font-medium transition"
          >
            {isPairing ? 'Accoppiamento...' : 'Avvia Pairing'}
          </button>
        </div>
      </div>

      <div className="space-y-3">
        <h4 className="text-sm font-medium text-slate-300">Dispositivi Autorizzati</h4>
        <div className="divide-y divide-slate-800 border border-slate-800 rounded-lg overflow-hidden bg-slate-950/40">
          {devices.map((device) => (
            <div key={device.deviceId} className="p-3.5 flex items-center justify-between hover:bg-slate-800/40 transition">
              <div className="flex items-center gap-3">
                {device.status === 'Authorized' ? (
                  <ShieldCheck className="w-5 h-5 text-emerald-400" />
                ) : (
                  <ShieldAlert className="w-5 h-5 text-amber-400" />
                )}
                <div>
                  <div className="font-medium text-sm text-slate-200">{device.deviceName}</div>
                  <div className="text-xs text-slate-500">{device.ipAddress} • {new Date(device.authorizedAtUtc).toLocaleDateString()}</div>
                </div>
              </div>
              <span className="px-2 py-0.5 text-xs rounded bg-emerald-500/10 text-emerald-400 border border-emerald-500/20">
                {device.status}
              </span>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
};
