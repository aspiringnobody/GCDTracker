using GCDTracker;
using GCDTracker.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Tests
{
    [TestClass]
    public class Tests_Unit
    {
        [TestMethod]
        public unsafe void TestReadStringFromPointer_WithValidString_ReturnsCorrectString() {
            byte[] buffer = Encoding.UTF8.GetBytes("Blizzard III");
            fixed (byte* ptr = buffer) {
                byte* ptr2 = ptr;
                string result = HelperMethods.ReadStringFromPointer(&ptr2);
                Assert.AreEqual("Blizzard III", result);
            }
        }
        [TestMethod]
        public unsafe void TestReadStringFromPointer_WithNullPointer_ReturnsEmptyString() {
            byte* ptr = null;
            string result = HelperMethods.ReadStringFromPointer(&ptr);
            Assert.AreEqual("", result);
        }
        [TestMethod]
        public unsafe void TestReadStringFromPointer_WithJaggedString_ReturnsCorrectString() {
            byte[] buffer = Encoding.UTF8.GetBytes("Blizzard III\0");
            fixed (byte* ptr = buffer) {
                byte* ptr2 = ptr;
                string result = HelperMethods.ReadStringFromPointer(&ptr2);
                Assert.AreEqual("Blizzard III", result);
            }
        }
        [TestMethod]
        public unsafe void TestReadStringFromPointer_WithJapaneseString_ReturnsCorrectString() {
            byte[] buffer = Encoding.UTF8.GetBytes("ブリザガ");
            fixed (byte* ptr = buffer) {
                byte* ptr2 = ptr;
                string result = HelperMethods.ReadStringFromPointer(&ptr2);
                Assert.AreEqual("ブリザガ", result);
            }
        }
    }
}