using System;
using System.Collections.Generic;

static class extensions
{
    public static byte setbit(ref this byte thebyte, byte bit)
    {
        double pow = Math.Pow(2, (double)bit);
        byte or = (byte)(int)(pow);
        thebyte = (byte)((int)thebyte | (int)or);
        return thebyte;
    }

    public static byte unsetbit(ref this byte thebyte, byte bit)
    {
        double pow = Math.Pow(2, (double)bit);
        byte or = (byte)(int)(pow);
        if ((or & thebyte) == thebyte)
        {
            thebyte = (byte)((int)thebyte ^ (int)or);
        }
        return thebyte;
    }

    public static byte flipbit(ref this byte thebyte, byte bit)
    {
        double pow = Math.Pow(2, (double)bit);
        byte or = (byte)(int)(pow);
        thebyte = (byte)((int)thebyte ^ (int)or);
        return thebyte;
    }

    public static byte setbit(ref this byte thebyte, byte bit, bool value)
    {
        double pow = Math.Pow(2, (double)bit);
        byte or = (byte)(int)(pow);
        if ((or & thebyte) == thebyte && value == false)
        {
            thebyte = (byte)((int)thebyte ^ (int)or);
        }else if (value)
        {
            thebyte = (byte)((int)thebyte | (int)or);
        }
        return thebyte;
    }

    public static bool getbit(this byte thebyte, byte bit)
    {
        double pow = Math.Pow(2, (double)bit);
        byte or = (byte)(int)(pow);
        if ((or & thebyte) == thebyte)
        {
            return true;
        }
        else
        {
            return false;
        }
    }
}

namespace NesCPUEmulator
{

    using static extensions;


    class byteex
    {
        public byte bval;

        byteex(byte value)
        {
            bval = value;
        }

        public byteex flipbit(byte bit)
        {
            double pow = Math.Pow(2, (double)bit);
            byte or = (byte)(int)(pow);
            bval = (byte)((int)bval ^ (int)or);
            return this;
        }

        public static implicit operator byteex(byte value)
        {
            return new byteex(value);
        }

        public static implicit operator byte(byteex value)
        {
            return value.bval;
        }

        public byte this[byte bit]
        {
            get
            {
                return bval.getbit(bit) ? (byte)1 : (byte)0;
            }

            set
            {
                bval.setbit(bit,value == 1 ? true : false);
            }
        }
    }

    class memory
    {
        //2048 bytes
        //mirrored 3 times (from 2048 to 4095, then from 4096 to 6143, finally 6144 to 8191)


        //8 bytes (PPU) (at 8192->8199)
        //repeated from 8200 to 16383

        //‭16384‬ to 16407 APU / IO registers

        //16408 to 16415 disabled functionality

        //16416 to 65535 cartridge space

        public memory()
        {

        }

        public memory(byte[] program)
        {
            for (var i = 0; i < program.Length; i++)
            {
                this[(ushort)i] = program[i];
            }
        }

        byte[] addresses = new byte[51206];

        private UInt16 realAddr(UInt16 mappedAddr)
        {
            if (mappedAddr <= 8191)
            {
                return (ushort)(mappedAddr % 2048);
            }else if (mappedAddr <= 16383)
            {
                return (ushort)((short)((mappedAddr - 8192) % 8) + (short)(2048));
            }else if (mappedAddr <= 16407)
            {
                return (ushort)(2048 + 8 + mappedAddr);
            }
            else if (mappedAddr <= 16415)
            {
                return (ushort)(2048 + 8 + 24 +  mappedAddr);
            }
            else
            {
                return (ushort)(2048 + 8 + 8 + 24 + mappedAddr);
            }
        }

        private byte getval(UInt16 addr)
        {
            return addresses[realAddr(addr)];
        }

        private void setval(UInt16 addr, byte val)
        {
            addresses[realAddr(addr)] = val;
        }

        public byte this[UInt16 addr]
        {
            get
            {
                return getval(addr);
            }

            set
            {
                setval(addr, value);
            }
        }
    }

    class CPU
    {

        private const bool accurateOverflagsInDecimalMode = false;

        enum flag
        {
            /*
             7  bit  0
            ---- ----
            NVss DIZC
            |||| ||||
            |||| |||+- Carry: 1 if last addition or shift resulted in a carry, or if
            |||| |||     last subtraction resulted in no borrow
            |||| ||+-- Zero: 1 if last operation resulted in a 0 value
            |||| |+--- Interrupt: Interrupt inhibit
            |||| |       (0: /IRQ and /NMI get through; 1: only /NMI gets through)
            |||| +---- Decimal: 1 to make ADC and SBC use binary-coded decimal arithmetic
            ||||         (ignored on second-source 6502 like that in the NES)
            ||++------ s: No effect, used by the stack copy, see note below
            |+-------- Overflow: 1 if last ADC or SBC resulted in signed overflow,
            |            or D6 from last BIT
            +--------- Negative: Set to bit 7 of the last operation
                 */

            Carry = 0,
            Zero = 1,
            Interrupt = 2,
            Decimal = 3,
            Break = 4,
            S2 = 5,
            Overflow = 6,
            Negative = 7

        }

        struct NesCPU
        {
            private byte aval;

            public byte A {
                set
                {
                    byteex val = value;
                    if (val[7] == 1)
                    {
                        P[(byte)flag.Negative] = 1;
                    }
                    else
                    {
                        P[(byte)flag.Negative] = 0;
                    }
                    if (value == 0)
                    {
                        P[(byte)flag.Zero] = 1;
                    }
                    else
                    {
                        P[(byte)flag.Zero] = 0;
                    }
                    aval = val;
                }

                get
                {
                    return aval;
                }

            } //Accumulator
            public byte X; //Index X
            public byte Y; //Index Y
            public ushort PC; //Program Counter
            public byte SP; //Stack Pointer
            public byteex P; //Status Register (actually only 6 bits)

        }

        private NesCPU states;

        public bool CPUActive = true;

        public CPU()
        {
            states.P = 0;
            states.PC = 0;
            states.SP = 0;
            states.X = 0;
            states.Y = 0;
        }

        public CPU(ref memory MemorySpace)
        {
            Memory = MemorySpace;
            states.P = 0;
            states.PC = 0;
            states.SP = 0;
            states.X = 0;
            states.Y = 0;
        }

        private int nops = 0;

        public enum ops
        {
            BRK = 0,
            ORA = 1,
            STP = 2,
            SLO = 3,
            NOP = 4,
            ASL = 5,
            PHP = 6,
            ANC = 7,
            BPL = 8,
            CLC = 9,
            JSR = 10,
            AND = 11,
            RLA = 12,
            BIT = 13,
            ROL = 14,
            PLP = 15,
            BMI = 16,
            SEC = 17,
            RTI = 18,
            EOR = 19,
            SRE = 20,
            LSR = 21,
            PHA = 22,
            ALR = 23,
            JMP = 24,
            BVC = 25,
            CLI = 26,
            RTS = 27,
            ADC = 28,
            RRA = 29,
            ROR = 30,
            PLA = 31,
            ARR = 32,
            BVS = 33,
            SEI = 34,
            STA = 35,
            SAX = 36,
            STY = 37,
            DEY = 38,
            TXA = 39,
            STX = 40,
            XAA = 41,
            BCC = 42,
            AHX = 43,
            TYA = 44,
            TXS = 45,
            TAS = 46,
            SHY = 47,
            SHX = 48,
            LDY = 49,
            LDA = 50,
            LDX = 51,
            LAX = 52,
            TAY = 53,
            BCS = 54,
            CLV = 55,
            TAX = 56,
            TSX = 57,
            LAS = 58,
            CPY = 59,
            CMP = 60,
            DCP = 61,
            DEC = 62,
            INY = 63,
            DEX = 64,
            AXS = 65,
            BNE = 66,
            CLD = 67,
            CPX = 68,
            SBC = 69,
            ISC = 70,
            INC = 71,
            INX = 72,
            BEQ = 73,
            SED = 74
        }

        private byte[] opmap = {
            0 , 1 , 2 , 3 , 4 , 1 , 5 , 3 , 6 , 1 , 5 , 7 , 4 , 1 , 5 , 3 , 8 , 1 , 2 , 3 , 4 , 1 , 5 , 3 , 9 , 1 , 4 , 3 , 4 , 1 , 5 , 3 , 10 , 11 , 2 , 12 , 13 , 11 , 14 , 12 , 15 , 11 , 14 , 7 , 13 , 11 , 14 , 12 , 16 , 11 , 2 , 12 , 4 , 11 , 14 , 12 , 17 , 11 , 4 , 12 , 4 , 11 , 14 , 12 , 18 , 19 , 2 , 20 , 4 , 19 , 21 , 20 , 22 , 19 , 21 , 23 , 24 , 19 , 21 , 20 , 25 , 19 , 2 , 20 , 4 , 19 , 21 , 20 , 26 , 19 , 4 , 20 , 4 , 19 , 21 , 20 , 27 , 28 , 2 , 29 , 4 , 28 , 30 , 29 , 31 , 28 , 30 , 32 , 24 , 28 , 30 , 29 , 33 , 28 , 2 , 29 , 4 , 28 , 30 , 29 , 34 , 28 , 4 , 29 , 4 , 28 , 30 , 29 , 4 , 35 , 4 , 36 , 37 , 35 , 40 , 36 , 38 , 4 , 39 , 41 , 37 , 35 , 40 , 36 , 42 , 35 , 2 , 43 , 37 , 35 , 40 , 36 , 44 , 35 , 45 , 46 , 47 , 35 , 48 , 43 , 49 , 50 , 51 , 52 , 49 , 50 , 51 , 52 , 53 , 50 , 56 , 52 , 49 , 50 , 51 , 52 , 54 , 50 , 2 , 52 , 49 , 50 , 51 , 52 , 55 , 50 , 57 , 58 , 49 , 50 , 51 , 52 , 59 , 60 , 4 , 61 , 59 , 60 , 62 , 61 , 63 , 60 , 64 , 65 , 59 , 60 , 62 , 61 , 66 , 60 , 2 , 61 , 4 , 60 , 62 , 61 , 67 , 60 , 4 , 61 , 4 , 60 , 62 , 61 , 68 , 69 , 4 , 70 , 68 , 69 , 71 , 70 , 72 , 69 , 4 , 69 , 68 , 69 , 71 , 70 , 73 , 69 , 2 , 70 , 4 , 69 , 71 , 70 , 74 , 69 , 4 , 70 , 4 , 69 , 71 , 70
        };

        public enum addressingMode
        {
            None = 0,
            Relative = 1,
            Absolute = 2,
            AbsoluteY = 3,
            AbsoluteX = 4,
            Indirect = 5,
            Immediate = 6,
            IndexedIndirect = 7,
            IndirectIndexed = 8,
            ZeroPage = 9,
            ZeroPageY = 10,
            ZeroPageX = 11
        }

        private byte[] addressingModeMap = {
            0, 7, 0, 7, 9, 9, 9, 9, 0, 6, 0, 6, 2, 2, 2, 2, 1, 8, 0, 8, 11, 11, 11, 11, 0, 3, 0, 3, 4, 4, 4, 4, 2, 7, 0, 7, 9, 9, 9, 9, 0, 6, 0, 6, 2, 2, 2, 2, 1, 8, 0, 8, 11, 11, 11, 11, 0, 3, 0, 3, 4, 4, 4, 4, 0, 7, 0, 7, 9, 9, 9, 9, 0, 6, 0, 6, 2, 2, 2, 2, 1, 8, 0, 8, 11, 11, 11, 11, 0, 3, 0, 3, 4, 4, 4, 4, 0, 7, 0, 7, 9, 9, 9, 9, 0, 6, 0, 6, 5, 2, 2, 2, 1, 8, 0, 8, 11, 11, 11, 11, 0, 3, 0, 3, 4, 4, 4, 4, 6, 7, 6, 7, 9, 9, 9, 9, 0, 6, 0, 6, 2, 2, 2, 2, 1, 8, 0, 8, 11, 11, 10, 10, 0, 3, 0, 3, 4, 4, 3, 3, 6, 7, 6, 7, 9, 9, 9, 9, 0, 6, 0, 6, 2, 2, 2, 2, 1, 8, 0, 8, 11, 11, 10, 10, 0, 3, 0, 3, 4, 4, 3, 3, 6, 7, 6, 7, 9, 9, 9, 9, 0, 6, 0, 6, 2, 2, 2, 2, 1, 8, 0, 8, 11, 11, 11, 11, 0, 3, 0, 3, 4, 4, 4, 4, 6, 7, 6, 7, 9, 9, 9, 9, 0, 6, 0, 6, 2, 2, 2, 2, 1, 8, 0, 8, 11, 11, 11, 11, 0, 3, 0, 3, 4, 4, 4, 4
        };

        private memory Memory = new memory();

        private byte[] addressingBytes = {
            0,
            1,
            2,
            2,
            2,
            2,
            1,
            1,
            1,
            1,
            1,
            1
        };

        private void incrementProgramCounter(addressingMode mode)
        {
            states.PC += (ushort)(1 + addressingBytes[(int)mode]);
        }

        private void pushToStack(byte value)
        {
            byte spval = states.SP;
            Memory[(ushort)(spval + 256)] = value;
            states.SP -= 1;
        }

        private byte pullFromStack()
        {
            states.SP += 1;
            byte val = Memory[(ushort)(states.SP + 256)];
            //Memory[(ushort)(states.SP + 256)] = 0x00;
            return val;
        }

        public bool readyForInstruction()
        {
            if (nops > 1)
            {
                nops--;
                return false;
            }
            else
            {
                nops = 0;
                return true;
            }
        }

        public void runProgram(byte[] program)
        {
            CPUActive = true;

            for (var i = 0; i < program.Length; i++)
            {
                Memory[(ushort)i] = program[i];
            }

            while (true && CPUActive)
            {
                runInstructionAt(states.PC);
            }

        }

        public void runEntireProgram(ushort from = 0)
        {
            CPUActive = true;
            states.PC = from;
            while (CPUActive)
            {
                runInstructionAt(states.PC);
            }
        }

        public void runInstructionAt(ushort memaddress)
        {
            byte operation = Memory[memaddress];
            ops opname = (ops)opmap[operation];
            addressingMode addressingMode = (addressingMode)addressingModeMap[operation];

            int memaddr = findMemAddrFromAddressingMode(addressingMode, new byte[] { Memory[memaddress], Memory[(ushort)(memaddress + 1)], Memory[(ushort)(memaddress + 2)], Memory[(ushort)(memaddress + 3)], Memory[(ushort)(memaddress + 4)] });
            byte dataval;

            if (memaddr < 0 && memaddr > -500)
            {
                dataval = (byte)(-(memaddr));
            }
            else
            {
                dataval = Memory[(ushort)memaddr];
            }

            switch (opname)
            {
                case ops.ADC:
                    ADC(dataval);
                    incrementProgramCounter(addressingMode);
                    break;
                case ops.AND:
                    AND(dataval);
                    incrementProgramCounter(addressingMode);
                    break;
                case ops.ASL:
                    if (addressingMode == addressingMode.None)
                    {
                        ASLA();
                    }
                    else
                    {
                        ASL(ref dataval);
                        Memory[(ushort)memaddr] = dataval;
                    }
                    incrementProgramCounter(addressingMode);
                    break;
                case ops.BCC:
                    incrementProgramCounter(addressingMode);
                    BCC(dataval);
                    break;
                case ops.BCS:
                    incrementProgramCounter(addressingMode);
                    BCS(dataval);
                    break;
                case ops.BEQ:
                    incrementProgramCounter(addressingMode);
                    BEQ(dataval);
                    break;
                case ops.BIT:
                    BIT(dataval);
                    break;
                case ops.BMI:
                    incrementProgramCounter(addressingMode);
                    BMI(dataval);
                    break;
                case ops.BNE:
                    incrementProgramCounter(addressingMode);
                    BNE(dataval);
                    break;
                case ops.BPL:
                    incrementProgramCounter(addressingMode);
                    BPL(dataval);
                    break;
                case ops.BRK:
                    incrementProgramCounter(addressingMode);
                    BRK();
                    break;
                case ops.BVC:
                    incrementProgramCounter(addressingMode);
                    BVC(dataval);
                    break;
                case ops.BVS:
                    incrementProgramCounter(addressingMode);
                    BVS(dataval);
                    break;
                case ops.CLC:
                    incrementProgramCounter(addressingMode);
                    CLC();
                    break;
                case ops.CLD:
                    incrementProgramCounter(addressingMode);
                    CLD();
                    break;
                case ops.CLI:
                    incrementProgramCounter(addressingMode);
                    CLI();
                    break;
                case ops.CLV:
                    incrementProgramCounter(addressingMode);
                    CLV();
                    break;
                case ops.CMP:
                    incrementProgramCounter(addressingMode);
                    CMP(dataval);
                    break;
                case ops.CPX:
                    incrementProgramCounter(addressingMode);
                    CPX();
                    break;
                case ops.CPY:
                    incrementProgramCounter(addressingMode);
                    CPY();
                    break;
                case ops.DEC:
                    incrementProgramCounter(addressingMode);
                    DEC(ref dataval);
                    Memory[(ushort)memaddr] = dataval;
                    break;
                case ops.DEX:
                    incrementProgramCounter(addressingMode);
                    DEX();
                    break;
                case ops.DEY:
                    incrementProgramCounter(addressingMode);
                    DEY();
                    break;
                case ops.EOR:
                    incrementProgramCounter(addressingMode);
                    EOR(dataval);
                    break;
                case ops.INC:
                    incrementProgramCounter(addressingMode);
                    INC(ref dataval);
                    Memory[(ushort)memaddr] = dataval;
                    break;
                case ops.INX:
                    incrementProgramCounter(addressingMode);
                    INX();
                    break;
                case ops.INY:
                    incrementProgramCounter(addressingMode);
                    INY();
                    break;
                case ops.JMP:
                    JMP((ushort)memaddr);
                    break;
                case ops.JSR:
                    JSR((ushort)memaddr);
                    break;
                case ops.LDA:
                    incrementProgramCounter(addressingMode);
                    LDA(dataval);
                    break;
                case ops.LDX:
                    incrementProgramCounter(addressingMode);
                    LDX(dataval);
                    break;
                case ops.LDY:
                    incrementProgramCounter(addressingMode);
                    LDY(dataval);
                    break;
                case ops.LSR:
                    incrementProgramCounter(addressingMode);
                    if (addressingMode == addressingMode.None)
                    {
                        LSRA();
                    }
                    else
                    {
                        LSR(ref dataval);
                        Memory[(ushort)memaddr] = dataval;
                    }
                    break;
                case ops.NOP:
                    incrementProgramCounter(addressingMode.None);
                    break;
                case ops.ORA:
                    incrementProgramCounter(addressingMode);
                    ORA(dataval);
                    break;
                case ops.PHA:
                    incrementProgramCounter(addressingMode);
                    PHA();
                    break;
                case ops.PHP:
                    incrementProgramCounter(addressingMode);
                    PHP();
                    break;
                case ops.PLA:
                    incrementProgramCounter(addressingMode);
                    PLA();
                    break;
                case ops.PLP:
                    incrementProgramCounter(addressingMode);
                    PLP();
                    break;
                case ops.SED:
                    incrementProgramCounter(addressingMode);
                    SED();
                    break;
                case ops.STP:
                    CPUActive = false;
                    break;
                case ops.ROL:
                    incrementProgramCounter(addressingMode);
                    if (addressingMode == addressingMode.None)
                    {
                        ROLA();
                    }
                    else
                    {
                        ROL(ref dataval);
                        Memory[(ushort)memaddr] = dataval;
                    }
                    break;
                case ops.ROR:
                    incrementProgramCounter(addressingMode);
                    if (addressingMode == addressingMode.None)
                    {
                        RORA();
                    }
                    else
                    {
                        ROR(ref dataval);
                        Memory[(ushort)memaddr] = dataval;
                    }
                    break;
                case ops.RTI:
                    RTI();
                    incrementProgramCounter(addressingMode);
                    break;
                case ops.RTS:
                    RTS();
                    incrementProgramCounter(addressingMode);
                    break;
                case ops.SBC:
                    SBC(dataval);
                    incrementProgramCounter(addressingMode);
                    break;
                case ops.SEC:
                    SEC();
                    incrementProgramCounter(addressingMode);
                    break;
                case ops.SEI:
                    SEI();
                    incrementProgramCounter(addressingMode);
                    break;
                case ops.STA:
                    STA(ref dataval);
                    Memory[(ushort)memaddr] = dataval;
                    incrementProgramCounter(addressingMode);
                    break;
                case ops.STX:
                    STX(ref dataval);
                    Memory[(ushort)memaddr] = dataval;
                    incrementProgramCounter(addressingMode);
                    break;
                case ops.STY:
                    STY(ref dataval);
                    Memory[(ushort)memaddr] = dataval;
                    incrementProgramCounter(addressingMode);
                    break;
                case ops.TAX:
                    TAX();
                    incrementProgramCounter(addressingMode);
                    break;
                case ops.TAY:
                    TAY();
                    incrementProgramCounter(addressingMode);
                    break;
                case ops.TSX:
                    TSX();
                    incrementProgramCounter(addressingMode);
                    break;
                case ops.TXA:
                    TXA();
                    incrementProgramCounter(addressingMode);
                    break;
                case ops.TXS:
                    TXS();
                    incrementProgramCounter(addressingMode);
                    break;
                case ops.TYA:
                    TYA();
                    incrementProgramCounter(addressingMode);
                    break;
                default:
                    throw new NotImplementedException("That instruction (" + opname.ToString() + ") hasn't been implemented.");

            }
        }

        public void runInstruction(byte[] instruction)
        {
            byte operation = instruction[0];
            ops opname = (ops)opmap[operation];
            addressingMode addressingMode = (addressingMode)addressingModeMap[operation];

            int memaddr = findMemAddrFromAddressingMode(addressingMode, instruction);
            byte dataval;

            if (memaddr < 0 && memaddr > -500)
            {
                dataval = (byte)(-(memaddr));
            }
            else
            {
                dataval = Memory[(ushort)memaddr];
            }
            
            switch (opname)
            {
                case ops.ADC:
                    ADC(dataval);
                    incrementProgramCounter(addressingMode);
                    break;
                case ops.AND:
                    AND(dataval);
                    incrementProgramCounter(addressingMode);
                    break;
                case ops.ASL:
                    if (addressingMode == addressingMode.None)
                    {
                        ASLA();
                    }
                    else
                    {
                        ASL(ref dataval);
                        Memory[(ushort)memaddr] = dataval;
                    }
                    incrementProgramCounter(addressingMode);
                    break;
                case ops.BCC:
                    incrementProgramCounter(addressingMode);
                    BCC(dataval);
                    break;
                case ops.BCS:
                    incrementProgramCounter(addressingMode);
                    BCS(dataval);
                    break;
                case ops.BEQ:
                    incrementProgramCounter(addressingMode);
                    BEQ(dataval);
                    break;
                case ops.BIT:
                    BIT(dataval);
                    break;
                case ops.BMI:
                    incrementProgramCounter(addressingMode);
                    BMI(dataval);
                    break;
                case ops.BNE:
                    incrementProgramCounter(addressingMode);
                    BNE(dataval);
                    break;
                case ops.BPL:
                    incrementProgramCounter(addressingMode);
                    BPL(dataval);
                    break;
                case ops.BRK:
                    incrementProgramCounter(addressingMode);
                    BRK();
                    break;
                case ops.BVC:
                    incrementProgramCounter(addressingMode);
                    BVC(dataval);
                    break;
                case ops.BVS:
                    incrementProgramCounter(addressingMode);
                    BVS(dataval);
                    break;
                case ops.CLC:
                    incrementProgramCounter(addressingMode);
                    CLC();
                    break;
                case ops.CLD:
                    incrementProgramCounter(addressingMode);
                    CLD();
                    break;
                case ops.CLI:
                    incrementProgramCounter(addressingMode);
                    CLI();
                    break;
                case ops.CLV:
                    incrementProgramCounter(addressingMode);
                    CLV();
                    break;
                case ops.CMP:
                    incrementProgramCounter(addressingMode);
                    CMP(dataval);
                    break;
                case ops.CPX:
                    incrementProgramCounter(addressingMode);
                    CPX();
                    break;
                case ops.CPY:
                    incrementProgramCounter(addressingMode);
                    CPY();
                    break;
                case ops.DEC:
                    incrementProgramCounter(addressingMode);
                    DEC(ref dataval);
                    Memory[(ushort)memaddr] = dataval;
                    break;
                case ops.DEX:
                    incrementProgramCounter(addressingMode);
                    DEX();
                    break;
                case ops.DEY:
                    incrementProgramCounter(addressingMode);
                    DEY();
                    break;
                case ops.EOR:
                    incrementProgramCounter(addressingMode);
                    EOR(dataval);
                    break;
                case ops.INC:
                    incrementProgramCounter(addressingMode);
                    INC(ref dataval);
                    Memory[(ushort)memaddr] = dataval;
                    break;
                case ops.INX:
                    incrementProgramCounter(addressingMode);
                    INX();
                    break;
                case ops.INY:
                    incrementProgramCounter(addressingMode);
                    INY();
                    break;
                case ops.JMP:
                    JMP((ushort)memaddr);
                    break;
                case ops.JSR:
                    JSR((ushort)memaddr);
                    break;
                case ops.LDA:
                    incrementProgramCounter(addressingMode);
                    LDA(dataval);
                    break;
                case ops.LDX:
                    incrementProgramCounter(addressingMode);
                    LDX(dataval);
                    break;
                case ops.LDY:
                    incrementProgramCounter(addressingMode);
                    LDY(dataval);
                    break;
                case ops.LSR:
                    incrementProgramCounter(addressingMode);
                    if (addressingMode == addressingMode.None)
                    {
                        LSRA();
                    }
                    else
                    {
                        LSR(ref dataval);
                        Memory[(ushort)memaddr] = dataval;
                    }
                    break;
                case ops.NOP:
                    incrementProgramCounter(addressingMode.None);
                    break;
                case ops.ORA:
                    incrementProgramCounter(addressingMode);
                    ORA(dataval);
                    break;
                case ops.PHA:
                    incrementProgramCounter(addressingMode);
                    PHA();
                    break;
                case ops.PHP:
                    incrementProgramCounter(addressingMode);
                    PHP();
                    break;
                case ops.PLA:
                    incrementProgramCounter(addressingMode);
                    PLA();
                    break;
                case ops.PLP:
                    incrementProgramCounter(addressingMode);
                    PLP();
                    break;
                case ops.SED:
                    incrementProgramCounter(addressingMode);
                    SED();
                    break;
                case ops.ROL:
                    incrementProgramCounter(addressingMode);
                    if (addressingMode == addressingMode.None)
                    {
                        ROLA();
                    }
                    else
                    {
                        ROL(ref dataval);
                        Memory[(ushort)memaddr] = dataval;
                    }
                    break;
                case ops.ROR:
                    incrementProgramCounter(addressingMode);
                    if (addressingMode == addressingMode.None)
                    {
                        RORA();
                    }
                    else
                    {
                        ROR(ref dataval);
                        Memory[(ushort)memaddr] = dataval;
                    }
                    break;
                case ops.RTI:
                    RTI();
                    incrementProgramCounter(addressingMode);
                    break;
                case ops.RTS:
                    RTS();
                    incrementProgramCounter(addressingMode);
                    break;
                case ops.SBC:
                    SBC(dataval);
                    incrementProgramCounter(addressingMode);
                    break;
                case ops.SEC:
                    SEC();
                    incrementProgramCounter(addressingMode);
                    break;
                case ops.SEI:
                    SEI();
                    incrementProgramCounter(addressingMode);
                    break;
                case ops.STA:
                    STA(ref dataval);
                    Memory[(ushort)memaddr] = dataval;
                    incrementProgramCounter(addressingMode);
                    break;
                case ops.STX:
                    STX(ref dataval);
                    Memory[(ushort)memaddr] = dataval;
                    incrementProgramCounter(addressingMode);
                    break;
                case ops.STY:
                    STY(ref dataval);
                    Memory[(ushort)memaddr] = dataval;
                    incrementProgramCounter(addressingMode);
                    break;
                case ops.TAX:
                    TAX();
                    incrementProgramCounter(addressingMode);
                    break;
                case ops.TAY:
                    TAY();
                    incrementProgramCounter(addressingMode);
                    break;
                case ops.TSX:
                    TSX();
                    incrementProgramCounter(addressingMode);
                    break;
                case ops.TXA:
                    TXA();
                    incrementProgramCounter(addressingMode);
                    break;
                case ops.TXS:
                    TXS();
                    incrementProgramCounter(addressingMode);
                    break;
                case ops.TYA:
                    TYA();
                    incrementProgramCounter(addressingMode);
                    break;
                default:
                    throw new NotImplementedException("That instruction (" + opname.ToString() + ") hasn't been implemented.");

            }

        }

        #region

        private byte ByteToDecimal(byte value)
        {
            int lower = value % 16;
            int upper = (int)Math.Floor((double)value / 16) * 10;
            return (byte)(lower + upper);
        }

        private byte DecimalToByte(byte value)
        {
            int tens = (int)Math.Floor((double)value / 10);
            int ones = value % 10;
            return (byte)(tens * 16 + ones);
        }

        private void ADC(byte data)
        {
            unchecked { 
                if (states.P[(byte)flag.Decimal] == 1)
                {
                    byte aval = ByteToDecimal(states.A);
                    byte bval = ByteToDecimal(data);

                    //ushort answervalue = (ushort)(aval + bval);
                    byte answer = DecimalToByte((byte)(aval + bval));
                    byte actualanswer = (byte)(aval + bval);

                    if (aval + bval > 256)
                    {
                        states.P[(byte)flag.Carry] = 1;
                    }
                    else
                    {
                        states.P[(byte)flag.Carry] = 0;
                    }

                    if ((((aval ^ actualanswer) & (bval ^ actualanswer)) & 0x80) > 0)
                    {
                        states.P[(byte)flag.Overflow] = 1;
                    }
                    else
                    {
                        states.P[(byte)flag.Overflow] = 0;
                    }

                    //(M^result)&(N^result)&0x80
                    states.A = answer;
                }
                else
                {
                    byte aval = states.A;
                    byte bval = data;
                    ushort answervalue = (ushort)(aval + bval);
                    byte answer = (byte)answervalue;

                    if (answervalue > 256)
                    {
                        states.P[(byte)flag.Carry] = 1;
                    }
                    else
                    {
                        states.P[(byte)flag.Carry] = 0;
                    }

                    if ((((aval ^ answer) & (bval ^ answer)) & 0x80) > 0)
                    {
                        states.P[(byte)flag.Overflow] = 1;
                    }
                    else
                    {
                        states.P[(byte)flag.Overflow] = 0;
                    }

                    states.A = answer;

                }
            }
        }

        private void AND(byte data)
        {
            states.A = (byte)(states.A & data);
        }

        //arithmetic shift on Accumulator
        private void ASLA()
        {
            unchecked
            {
                ushort val = (ushort)(states.A << 1);
                states.A = (byte)(val);
                if (val > 256)
                {
                    states.P[(byte)flag.Carry] = 1;
                }
                else
                {
                    states.P[(byte)flag.Carry] = 0;
                }
            }
            
        }

        //arithmetic shift on memory
        private void ASL(ref byte value)
        {
            unchecked
            {
                ushort val = (ushort)(value << 1);
                value = (byte)(val);
                if (val > 256)
                {
                    states.P[(byte)flag.Carry] = 1;
                }
                else
                {
                    states.P[(byte)flag.Carry] = 0;
                }
            }
        }

        private void BCC(byte offset)
        {
            unchecked
            {
                if (states.P[(byte)flag.Carry] == 0)
                {
                    sbyte signedOffset = (sbyte)offset;
                    states.PC = (ushort)(states.PC + signedOffset - 1);
                }
            }
        }

        private void BCS(byte offset)
        {
            unchecked
            {
                if (states.P[(byte)flag.Carry] == 1)
                {
                    sbyte signedOffset = (sbyte)offset;
                    states.PC = (ushort)(states.PC + signedOffset - 1);
                }
            }
        }

        private void BEQ(byte offset)
        {
            unchecked
            {
                if (states.P[(byte)flag.Zero] == 1)
                {
                    sbyte signedOffset = (sbyte)offset;
                    states.PC = (ushort)(states.PC + signedOffset - 1);
                }
            }
        }

        private void BIT(byteex value)
        {
            byte maskHit = (byte)(value & states.A);
            if (maskHit == 0)
            {
                states.P[(byte)flag.Zero] = 1;
            }

            states.P[(byte)flag.Overflow] = value[6];
            states.P[(byte)flag.Negative] = value[7];

        }

        private void BMI(byte offset)
        {
            unchecked
            {
                if (states.P[(byte)flag.Negative] == 1)
                {
                    sbyte signedOffset = (sbyte)offset;
                    states.PC = (ushort)(states.PC + signedOffset - 1);
                }
            }
        }

        private void BNE(byte offset)
        {
            unchecked
            {
                if (states.P[(byte)flag.Zero] == 0)
                {
                    sbyte signedOffset = (sbyte)offset;
                    states.PC = (ushort)(states.PC + signedOffset - 1);
                }
            }
        }

        private void BPL(byte offset)
        {
            unchecked
            {
                if (states.P[(byte)flag.Negative] == 0)
                {
                    sbyte signedOffset = (sbyte)offset;
                    states.PC = (ushort)(states.PC + signedOffset - 1);
                }
            }
        }

        private void pushProgramCounterToStack(ushort programCounter)
        {
            ushort pc = programCounter;
            byte pchi = (byte)(pc >> 8);
            byte pclo = (byte)(pc ^ (pchi << 8));
            pushToStack(pchi);
            pushToStack(pclo);
        }

        private void BRK()
        {
            states.P[(byte)flag.Break] = 1;
            ushort newpc = (ushort)(Memory[0xFFFF] << 8 + Memory[0xFFFE]);
            pushProgramCounterToStack(states.PC);
            pushToStack(states.P);
            states.PC = newpc;
        }

        private void BVC(byte offset)
        {
            unchecked
            {
                if (states.P[(byte)flag.Overflow] == 0)
                {
                    sbyte signedOffset = (sbyte)offset;
                    states.PC = (ushort)(states.PC + signedOffset - 1);
                }
            }
        }

        private void BVS(byte offset)
        {
            unchecked
            {
                if (states.P[(byte)flag.Overflow] == 1)
                {
                    sbyte signedOffset = (sbyte)offset;
                    states.PC = (ushort)(states.PC + signedOffset - 1);
                }
            }
        }

        private void CLC()
        {
            states.P[(byte)flag.Carry] = 0;
        }

        private void CLD()
        {
            states.P[(byte)flag.Decimal] = 0;
        }

        private void CLI()
        {
            states.P[(byte)flag.Interrupt] = 0;
        }

        private void CLV()
        {
            states.P[(byte)flag.Overflow] = 0;
        }

        private void compare(int result)
        {
            if (result < 0)
            {
                states.P[(byte)flag.Carry] = 0;
                states.P[(byte)flag.Negative] = 1;
                states.P[(byte)flag.Zero] = 0;
            }
            else if (result == 0)
            {
                states.P[(byte)flag.Carry] = 0;
                states.P[(byte)flag.Negative] = 0;
                states.P[(byte)flag.Zero] = 1;
            }
            else
            {
                states.P[(byte)flag.Carry] = 1;
                states.P[(byte)flag.Negative] = 0;
                states.P[(byte)flag.Zero] = 0;
            }
        }

        private void CMP(byte val)
        {
            int result = states.A - val;
            compare(result);
        }

        private void CPX()
        {
            int result = states.A - states.X;
            compare(result);

        }

        private void CPY()
        {
            int result = states.A - states.Y;
            compare(result);

        }

        private void valueCheck(ref byte value)
        {
            if (value == 0)
            {
                states.P[(byte)flag.Zero] = 1;
            }
            else
            {
                states.P[(byte)flag.Zero] = 0;
            }

            if (value >= 128)
            {
                states.P[(byte)flag.Negative] = 1;
            }
            else
            {
                states.P[(byte)flag.Negative] = 0;
            }
        }

        #endregion

        #region
        private void DEC(ref byte value)
        {
            unchecked
            {
                value = (byte)(value - 1);
                valueCheck(ref value);
            }
        }

        private void DEX()
        {
            unchecked
            {
                byte value = (byte)(states.X - 1);
                valueCheck(ref value);

                states.X = value;
            }
        }

        private void DEY()
        {
            unchecked
            {
                byte value = (byte)(states.Y - 1);
                valueCheck(ref value);
                states.Y = value;
            }
        }

        private void EOR(byte value)
        {
            states.A = (byte)(states.A ^ value);
        }

        private void INC(ref byte value)
        {
            unchecked
            {
                value = (byte)(value + 1);
                valueCheck(ref value);
            }
        }

        private void INX()
        {
            unchecked
            {
                byte value = (byte)(states.X + 1);
                valueCheck(ref value);

                states.X = value;
            }
        }

        private void INY()
        {
            unchecked
            {
                byte value = (byte)(states.Y + 1);
                valueCheck(ref value);

                states.Y = value;
            }
        }

        private void JMP(ushort memaddr)
        {
            states.PC = (ushort)(memaddr - 1);
        }

        private void JSR(ushort memaddr)
        {
            pushProgramCounterToStack((ushort)(states.PC + 2));
            states.PC = (ushort)(memaddr);
        }

        private void LDA(byte value)
        {
            states.A = value;
        }

        private void LDX(byte value)
        {
            states.X = value;
        }

        private void LDY(byte value)
        {
            states.Y = value;
        }

        private void LSRA()
        {
            unchecked
            {

                states.P[(byte)flag.Carry] = ((byteex)states.A)[0];

                ushort val = (ushort)(states.A >> 1);
                states.A = (byte)(val);
                
            }

        }

        private void LSR(ref byte value)
        {
            unchecked
            {

                states.P[(byte)flag.Carry] = ((byteex)value)[0];
                value = (byte)(value >> 1);

            }
        }

        private void NOP()
        {

        }

        private void ORA(byte mem)
        {
            states.A = (byte)(states.A | mem);
        }

        #endregion

        #region
        private void PHA()
        {
            pushToStack(states.A);
        }

        private void PHP()
        {
            pushToStack(states.P);
        }

        private void PLA()
        {
            states.A = pullFromStack();
        }

        private void PLP()
        {
            states.P = pullFromStack();
        }

        private void SED()
        {
            states.P[(byte)flag.Decimal] = 1;
        }

        private void ROLA()
        {
            unchecked
            {
                byte carryFlag = states.P[(byte)flag.Carry];
                states.P[(byte)flag.Carry] = ((byteex)states.A)[7];
                ushort val = (ushort)(states.A << 1);
                byteex theval = (byteex)(val);
                theval[0] = carryFlag;
                states.A = (byte)(theval);
            }
        }

        private void ROL(ref byte memval)
        {
            unchecked
            {
                byte carryFlag = states.P[(byte)flag.Carry];
                states.P[(byte)flag.Carry] = ((byteex)memval)[7];
                ushort val = (ushort)(memval << 1);
                byteex theval = (byteex)(val);
                theval[0] = carryFlag;
                memval = (byte)(theval);
            }
        }

        private void RORA()
        {
            unchecked
            {
                byte carryFlag = states.P[(byte)flag.Carry];
                states.P[(byte)flag.Carry] = ((byteex)states.A)[0];
                ushort val = (ushort)(states.A >> 1);
                byteex theval = (byteex)(val);
                theval[7] = carryFlag;
                states.A = (byte)(theval);
            }
        }

        private void ROR(ref byte memval)
        {
            unchecked
            {
                byte carryFlag = states.P[(byte)flag.Carry];
                states.P[(byte)flag.Carry] = ((byteex)memval)[0];
                ushort val = (ushort)(memval >> 1);
                byteex theval = (byteex)(val);
                theval[7] = carryFlag;
                memval = (byte)(theval);
            }
        }

        private ushort pullProgramCounterFromStack()
        {
            byte pclo = pullFromStack();
            byte pchi = pullFromStack();
            return (ushort)(pchi << 8 + pclo);
        }

        private void RTI()
        {
            states.P = pullFromStack();
            states.PC = pullProgramCounterFromStack();
        }

        private void RTS()
        {
            states.PC = pullProgramCounterFromStack();
        }

        private void SBC(byte data)
        {
            unchecked
            {
                if (states.P[(byte)flag.Decimal] == 1)
                {
                    byte aval = ByteToDecimal(states.A);
                    byte bval = ByteToDecimal(data);

                    //ushort answervalue = (ushort)(aval + bval);
                    byte answer = DecimalToByte((byte)(aval - bval - states.P[(byte)flag.Carry]));

                    if (Math.Abs(bval + states.P[(byte)flag.Carry]) > aval)
                    {
                        states.P[(byte)flag.Carry] = 1;
                    }
                    else
                    {
                        states.P[(byte)flag.Carry] = 0;
                    }

                    if ((((aval ^ answer) & ((bval ^ answer))) & 0x80) > 0)
                    {
                        states.P[(byte)flag.Overflow] = 1;
                    }
                    else
                    {
                        states.P[(byte)flag.Overflow] = 0;
                    }

                    //(M^result)&(N^result)&0x80
                    states.A = answer;
                }
                else
                {
                    byte aval = states.A;
                    byte bval = data;
                    ushort answervalue = (ushort)(aval - bval - states.P[(byte)flag.Carry]);
                    byte answer = (byte)answervalue;

                    if (Math.Abs(bval + states.P[(byte)flag.Carry]) > aval)
                    {
                        states.P[(byte)flag.Carry] = 1;
                    }
                    else
                    {
                        states.P[(byte)flag.Carry] = 0;
                    }

                    if ((((aval ^ answer) & (bval ^ answer)) & 0x80) > 0)
                    {
                        states.P[(byte)flag.Overflow] = 1;
                    }
                    else
                    {
                        states.P[(byte)flag.Overflow] = 0;
                    }

                    states.A = answer;

                }
            }
        }

        #endregion

        private void SEC()
        {
            states.P[(byte)flag.Carry] = 1;
        }

        private void SEI()
        {
            states.P[(byte)flag.Interrupt] = 1;
        }

        private void STA(ref byte memval)
        {
            memval = states.A;
        }

        private void STX(ref byte memval)
        {
            memval = states.X;
        }

        private void STY(ref byte memval)
        {
            memval = states.Y;
        }

        private void TAX()
        {
            states.A = states.A;
            states.X = states.A;
        }

        private void TAY()
        {
            states.A = states.A;
            states.Y = states.A;
        }

        private void TSX()
        {
            states.X = states.SP;

            byteex x = states.X;

            states.P[(byte)flag.Zero] = (byte)(x == 0 ? 1 : 0);
            states.P[(byte)flag.Negative] = x[7];

        }

        private void TXA()
        {
            states.A = states.X;
        }

        private void TXS()
        {
            states.SP = states.X;
        }

        private void TYA()
        {
            states.A = states.Y;
        }

        private int findMemAddrFromAddressingMode(addressingMode mode, byte[] args, int offset = 0)
        {
            int val = 0;
            int indexa = 0;
            int indexb = 0;
            switch (mode){
                case addressingMode.Absolute:
                    val = args[1 + offset];
                    val = val << 8;
                    val += args[2 + offset];

                    break;
                case addressingMode.AbsoluteX:
                    val = args[1 + offset];
                    val = val << 8;
                    val += args[2 + offset];
                    val += states.X;

                    break;
                case addressingMode.AbsoluteY:
                    val = args[1 + offset];
                    val = val << 8;
                    val += args[2 + offset];
                    val += states.Y;

                    break;
                case addressingMode.Immediate:
                    val = -args[1 + offset];
                    break;
                case addressingMode.IndexedIndirect:
                    //val = PEEK((arg + X) % 256) + PEEK((arg + X + 1) % 256) * 256
                    indexa = (args[1 + offset] + states.X) % 256;
                    indexb = (args[1 + offset] + 1 + states.X) % 256;
                    val = Memory[(UInt16)indexb];
                    val = val << 8;
                    val += Memory[(UInt16)indexa];
                    break;
                case addressingMode.Indirect:
                    int addressloc = args[1 + offset];
                    addressloc = addressloc << 8;
                    addressloc += args[2 + offset];

                    val = Memory[(ushort)addressloc] + (ushort)((int)Memory[(ushort)((ushort)addressloc + (ushort)1)] << 8);

                    break;
                case addressingMode.IndirectIndexed:
                    //
                    //val = PEEK(arg) + PEEK((arg + 1) % 256) * 256 + Y
                    //
                    indexa = Memory[args[1 + offset]];
                    indexb = Memory[(ushort)((args[1 + offset] + 1) % 256)] << 8;
                    val = indexa + indexb + states.Y;

                    break;
                case addressingMode.None:
                    val = -2048;
                    break;
                case addressingMode.Relative:
                    val = states.PC;

                    unchecked
                    {
                        sbyte relativeOffset = (sbyte)args[1 + offset];
                        val += relativeOffset;
                    }

                    break;
                case addressingMode.ZeroPage:
                    val = args[1 + offset];
                    break;
                case addressingMode.ZeroPageX:
                    val = ((args[1 + offset] + states.X) % 256);
                    break;
                case addressingMode.ZeroPageY:
                    val = ((args[1 + offset] + states.Y) % 256);
                    break;
            }

            return val;

        }

    }

    class PPU
    {
        private memory Memory = new memory();
        public PPU(ref memory m)
        {
            Memory = m;
        }

    }

    class NES
    {
        public CPU Processor { get; set; }
        public PPU PictureProcessor { get; set; }
        public memory SharedMemory { get; set; }

        public NES()
        {
            memory m = new memory();
            SharedMemory = m;
            Processor = new CPU(ref m);
            PictureProcessor = new PPU(ref m);
        }
    }

    class Tests
    {
        public static void testMemoryAddressing()
        {
            memory mem = new memory();
            mem[0] = 200;
            mem[20] = 87;
            if (mem[0] == 200)
            {
                Console.WriteLine("Read-Write OK");
            }
            else
            {
                Console.WriteLine("Read-Write Not OK");
            }

            for (var i = 0; i < 8191; i += 2048)
            {
                if (mem[(ushort)(i)] == 200 && mem[(ushort)(i + 20)] == 87)
                {
                    Console.WriteLine("Mirroring Base Memory OK at " + i.ToString());
                }
                else
                {
                    Console.WriteLine("Mirroring Base memory not OK");
                }
            }

            mem[8192] = 45;

            for (var i = 8200; i < 16383; i += 8)
            {
                if (mem[(ushort)(i)] == 45)
                {
                    //Console.WriteLine("Mirroring PPU Memory OK at " + i.ToString());
                }
                else
                {
                    Console.WriteLine("Mirroring PPU not OK");
                }
            }

            byte mine = 255;
            unchecked
            {
                sbyte yours = (sbyte)mine;
                Console.WriteLine(yours);
            }

        }
    }

    class Compiler
    {

    }

    class Instruction
    {
        CPU.ops opname { get; set; }
        CPU.addressingMode address { get; set; }
        byte[] args { get; set; }
        public Instruction(CPU.ops op, CPU.addressingMode addr, List<byte> arg = null)
        {
            opname = op;
            address = addr;
            if (arg == null)
                args = arg.ToArray();
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            Tests.testMemoryAddressing();

            /*
            byteex a = 0;
            a[4] = 1;
            Console.WriteLine(a);
            a[4] = 0;
            Console.WriteLine(a);
            a.flipbit(3);
            */

            //Put 60 in the accumulator, then add 30, then store in memory address 7, then stop the program
            memory memoryShare = new memory(new byte[] {
                0xa9, 0x3c,
                0x69, 0x1e,
                0x85, 0x07,
                0x02 });

            CPU cpu = new CPU(ref memoryShare);

            cpu.runEntireProgram();

            Console.WriteLine(memoryShare[7]);

            //Console.WriteLine(a);
            
            Console.ReadLine();
        }
    }
}
