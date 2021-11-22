﻿using System;
using System.Collections.Generic;

using BizHawk.Emulation.Common;

namespace BizHawk.Emulation.Cores.Nintendo.Gameboy
{
	public partial class Gameboy : IDebuggable
	{
		public IDictionary<string, RegisterValue> GetCpuFlagsAndRegisters()
		{
			int[] data = new int[10];
			LibGambatte.gambatte_getregs(GambatteState, data);

			return new Dictionary<string, RegisterValue>
			{
				["PC"] = (ushort)(data[(int)LibGambatte.RegIndices.PC] & 0xffff),
				["SP"] = (ushort)(data[(int)LibGambatte.RegIndices.SP] & 0xffff),
				["A"] = (byte)(data[(int)LibGambatte.RegIndices.A] & 0xff),
				["B"] = (byte)(data[(int)LibGambatte.RegIndices.B] & 0xff),
				["C"] = (byte)(data[(int)LibGambatte.RegIndices.C] & 0xff),
				["D"] = (byte)(data[(int)LibGambatte.RegIndices.D] & 0xff),
				["E"] = (byte)(data[(int)LibGambatte.RegIndices.E] & 0xff),
				["F"] = (byte)(data[(int)LibGambatte.RegIndices.F] & 0xff),
				["H"] = (byte)(data[(int)LibGambatte.RegIndices.H] & 0xff),
				["L"] = (byte)(data[(int)LibGambatte.RegIndices.L] & 0xff),
				["ROM Bank"] = LibGambatte.gambatte_getrombank(GambatteState),
				["SRAM Bank"] = LibGambatte.gambatte_getsrambank(GambatteState)
			};
		}

		public void SetCpuRegister(string register, int value)
		{
			int[] data = new int[10];
			LibGambatte.gambatte_getregs(GambatteState, data);
			LibGambatte.RegIndices index = (LibGambatte.RegIndices)Enum.Parse(typeof(LibGambatte.RegIndices), register);
			data[(int)index] = value & (index <= LibGambatte.RegIndices.SP ? 0xffff : 0xff);
			LibGambatte.gambatte_setregs(GambatteState, data);
		}

		public bool CanStep(StepType type) => false;

		[FeatureNotImplemented]
		public void Step(StepType type) => throw new NotImplementedException();

		public long TotalExecutedCycles => Math.Max((long)_cycleCount, (long)callbackCycleCount);

		private const string systemBusScope = "System Bus";

		private MemoryCallbackSystem _memorycallbacks = new MemoryCallbackSystem(new[] { systemBusScope, "ROM", "VRAM", "SRAM", "WRAM", "OAM", "HRAM" });
		public IMemoryCallbackSystem MemoryCallbacks => _memorycallbacks;

		private LibGambatte.MemoryCallback _readcb;
		private LibGambatte.MemoryCallback _writecb;
		private LibGambatte.MemoryCallback _execcb;

		/// <summary>
		/// for use in dual core
		/// </summary>
		internal void ConnectMemoryCallbackSystem(MemoryCallbackSystem mcs)
		{
			_memorycallbacks = mcs;
			_memorycallbacks.ActiveChanged += SetMemoryCallbacks;
		}

		private void InitMemoryCallbacks()
		{
			LibGambatte.MemoryCallback CreateCallback(MemoryCallbackFlags flags, Func<bool> getHasCBOfType)
			{
				var rawFlags = (uint)flags;
				return (address, cycleOffset) =>
				{
					callbackCycleCount = _cycleCount + cycleOffset;
					if (getHasCBOfType())
					{
						MemoryCallbacks.CallMemoryCallbacks(address, 0, rawFlags, systemBusScope);
						if (address < 0x4000u) // always rom bank 0 for most mbcs (todo: edge mbcs where this doesn't apply)
						{
							MemoryCallbacks.CallMemoryCallbacks(address, 0, rawFlags, "ROM");
						}
						else if (address < 0x8000u) // rom bank x
						{
							var bank = LibGambatte.gambatte_getrombank(GambatteState); // this will return 1 in case there is no mbc (0 is valid for some mbcs too)
							address += (uint)(bank * 0x4000);
							address -= 0x4000u;
							MemoryCallbacks.CallMemoryCallbacks(address, 0, rawFlags, "ROM");
						}
						else if (address < 0xA000u) // vram (may be banked on CGB in CGB enhanced mode)
						{
							if (IsCGBMode() && !IsCGBDMGMode())
							{
								var bank = LibGambatte.gambatte_cpuread(GambatteState, 0xFF4F) & 1;
								address += (uint)(bank * 0x2000);
							}
							address -= 0x8000u;
							MemoryCallbacks.CallMemoryCallbacks(address, 0, rawFlags, "VRAM");
						}
						else if (address < 0xC000u) // sram (may be banked)
						{
							var bank = LibGambatte.gambatte_getsrambank(GambatteState); // this will return 0 in case there is only one bank
							address += (uint)(bank * 0xA000);
							address -= 0xA000u;
							MemoryCallbacks.CallMemoryCallbacks(address, 0, rawFlags, "SRAM");
						}
						else if (address < 0xD000u) // wram bank 0
						{
							address -= 0xC000u;
							MemoryCallbacks.CallMemoryCallbacks(address, 0, rawFlags, "WRAM");
						}
						else if (address < 0xE000u) // wram bank x (always one for dmg/cgb in dmg mode)
						{
							if (IsCGBMode() && !IsCGBDMGMode())
							{
								var bank = Math.Max(LibGambatte.gambatte_cpuread(GambatteState, 0xFF70) & 7, 1);
								address += (uint)(bank * 0x1000);
							}
							address -= 0xD000u;
							MemoryCallbacks.CallMemoryCallbacks(address, 0, rawFlags, "WRAM");
						}
						else if (address < 0xFE00u) // echo ram
						{
							// do we do something here?
						}
						else if (address < 0xFEA0u) // oam
						{
							address -= 0xFEA0u;
							MemoryCallbacks.CallMemoryCallbacks(address, 0, rawFlags, "OAM");
						}
						else if (address < 0xFF00u) // "extra" oam
						{
							// do we do something here?
						}
						else if (address < 0xFF80u) // mmio
						{
							// do we do something here?
						}
						else if (address < 0xFFFF) // hram
						{
							address -= 0xFF80u;
							MemoryCallbacks.CallMemoryCallbacks(address, 0, rawFlags, "HRAM");
						}
						else if (address == 0xFFFF) // ie reg
						{
							// do we do something here?
						}
						else
						{
							throw new InvalidOperationException("Core accessed invalid address???");
						}
					}
				};
			}

			_readcb = CreateCallback(MemoryCallbackFlags.AccessRead, () => MemoryCallbacks.HasReads);
			_writecb = CreateCallback(MemoryCallbackFlags.AccessWrite, () => MemoryCallbacks.HasWrites);
			_execcb = CreateCallback(MemoryCallbackFlags.AccessExecute, () => MemoryCallbacks.HasExecutes);

			_memorycallbacks.ActiveChanged += SetMemoryCallbacks;
		}

		private void SetMemoryCallbacks()
		{
			LibGambatte.gambatte_setreadcallback(GambatteState, MemoryCallbacks.HasReads ? _readcb : null);
			LibGambatte.gambatte_setwritecallback(GambatteState, MemoryCallbacks.HasWrites ? _writecb : null);
			LibGambatte.gambatte_setexeccallback(GambatteState, MemoryCallbacks.HasExecutes ? _execcb : null);
		}
	}
}
