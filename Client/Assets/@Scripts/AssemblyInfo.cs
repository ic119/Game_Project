using System.Runtime.CompilerServices;

// internal 클래스(ObjectPoolStorage 등)를 공개 API로 넓히지 않고 EditMode 테스트 어셈블리에서만 보이게 한다.
[assembly: InternalsVisibleTo("Game.Tests.EditMode")]
