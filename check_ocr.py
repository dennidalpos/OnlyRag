import sys  
import importlib.metadata as m  
print(sys.executable)  
print('paddlepaddle', m.version('paddlepaddle'))  
print('paddlepaddle-gpu', m.version('paddlepaddle-gpu'))  
print('paddle', m.version('paddle'))  
import paddle  
print('compiled', getattr(paddle.device, 'is_compiled_with_cuda', lambda: False)())  
print('cuda count', getattr(getattr(paddle.device, 'cuda', None), 'device_count', lambda: 0)())  
